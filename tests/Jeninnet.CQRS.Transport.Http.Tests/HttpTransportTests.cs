//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Http.Tests;

/// <summary>
/// HTTP adapter tests: the shared transport contract against an in-memory TestServer endpoint,
/// plus authentication failures and HMAC verification. The TestServer owns receive-side dispatch,
/// matching a remote HTTP service; the transport owns only the outbound request.
/// </summary>
[TestClass]
public sealed class HttpTransportTests : TransportContractTests {
    private const string _apiKey = "test-api-key";
    private const string _hmacSecret = "test-hmac-secret";

    private static readonly ConcurrentDictionary<string, IMessageTransportHandler[]> _handlers = new(StringComparer.Ordinal);
    private static WebApplication? _app;
    private static TestServer? _server;
    private static ServiceProvider? _provider;
    private static IHttpClientFactory? _httpClientFactory;
    private static int _counter;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext) {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        _app = builder.Build();
        _app.MapPost("/messages/{id}", HandleMessageAsync);
        _app.MapPost("/secure/{id}", HandleSecureMessageAsync);

        await _app.StartAsync(testContext.CancellationToken).ConfigureAwait(false);
        _server = _app.GetTestServer();

        var services = new ServiceCollection();
        services.AddHttpClient("test").ConfigurePrimaryHttpMessageHandler(() => _server.CreateHandler());
        _provider = services.BuildServiceProvider();
        _httpClientFactory = _provider.GetRequiredService<IHttpClientFactory>();
    }

    [ClassCleanup]
    public static async Task ClassCleanupAsync() {
        _provider?.Dispose();

        if(_app is not null) {
            await _app.StopAsync().ConfigureAwait(false);
            await _app.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    protected override Task<IMessageTransport> CreateTransportAsync(IMessageSerializer serializer, params IMessageTransportHandler[] handlers) {
        var id = Interlocked.Increment(ref _counter).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _handlers[$"/messages/{id}"] = handlers;

        var options = new HttpTransportOptions {
            Endpoint = $"http://localhost/messages/{id}",
            HttpClientName = "test",
        };

        return Task.FromResult<IMessageTransport>(new HttpTransport(
            Options.Create(options),
            _httpClientFactory!,
            serializer,
            new TransportNamingConvention($"cqrs.http.{id}")));
    }

    [TestMethod]
    public async Task ApiKeyFailure_IsAClearAuthenticationErrorAsync() {
        // Arrange
        var transport = CreateSecureTransport("wrong-key", _hmacSecret);
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            // Act
            var thrown = await CaptureAsync(() => transport.SendAsync(
                new TransportContractMessage("auth", 1),
                cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

            // Assert
            MSTestAssertions.That(() => thrown.Should().BeOfType<HttpTransportException>());
            var failure = (HttpTransportException)thrown!;
            MSTestAssertions.That(() => failure.StatusCode.Should().Be(HttpStatusCode.Unauthorized));
            MSTestAssertions.That(() => failure.Message.Should().Contain("authentication failed"));
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task HmacFailure_IsRejectedByTheReceiverAsync() {
        // Arrange
        var transport = CreateSecureTransport(_apiKey, "wrong-secret");
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            // Act
            var thrown = await CaptureAsync(() => transport.PublishAsync(
                new TransportContractMessage("hmac", 2),
                cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

            // Assert
            MSTestAssertions.That(() => thrown.Should().BeOfType<HttpTransportException>());
            MSTestAssertions.That(() => ((HttpTransportException)thrown!).StatusCode.Should().Be(HttpStatusCode.Forbidden));
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task ValidApiKeyAndSignature_AreAcceptedAsync() {
        // Arrange
        var transport = CreateSecureTransport(_apiKey, _hmacSecret);
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            // Act
            var thrown = await CaptureAsync(() => transport.PublishAsync(
                new TransportContractMessage("secure", 3),
                cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

            // Assert
            MSTestAssertions.That(() => thrown.Should().BeNull());
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public void AddCqrsHttpTransport_RegistersTheTransportAndSelectsExternalMode() {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCqrsHttpTransport(options => options.Endpoint = "http://localhost/messages");
        using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IMessageTransport>();
        var mode = provider.GetRequiredService<IOptions<CqrsTransportOptions>>().Value.Mode;

        // Assert
        MSTestAssertions.That(() => transport.Should().BeOfType<HttpTransport>());
        MSTestAssertions.That(() => mode.Should().Be(CqrsTransportMode.External));
    }

    private static HttpTransport CreateSecureTransport(string apiKey, string secret) {
        var options = new HttpTransportOptions {
            Endpoint = $"http://localhost/secure/{Guid.NewGuid():N}",
            HttpClientName = "test",
            ApiKey = apiKey,
            HmacSecret = secret,
        };

        return new HttpTransport(
            Options.Create(options),
            _httpClientFactory!,
            CreateSerializer(),
            new TransportNamingConvention("cqrs.http.secure"));
    }

    private static JsonMessageSerializer CreateSerializer() =>
        new(Options.Create(new JsonMessageSerializerOptions()));

    private static async Task<IResult> HandleMessageAsync(HttpContext context, string id) {
        var payload = await ReadBodyAsync(context).ConfigureAwait(false);
        var headers = ReadHeaders(context);
        var envelope = HttpTransportWire.ReadEnvelope(headers, payload);

        if(!_handlers.TryGetValue($"/messages/{id}", out var handlers) || handlers.Length is 0) {
            return Results.NotFound();
        }

        if(envelope.DeliveryMode is TransportDeliveryMode.Send) {
            await handlers[0].HandleAsync(envelope, context.RequestAborted).ConfigureAwait(false);
        } else {
            foreach(var handler in handlers) {
                await handler.HandleAsync(envelope, context.RequestAborted).ConfigureAwait(false);
            }
        }

        return Results.Accepted();
    }

    private static async Task<IResult> HandleSecureMessageAsync(HttpContext context, string id) {
        _ = id;

        if(!context.Request.Headers.TryGetValue(HttpTransportHeaders.ApiKey, out var providedKey) ||
           !string.Equals(providedKey.ToString(), _apiKey, StringComparison.Ordinal)) {
            return Results.Unauthorized();
        }

        var payload = await ReadBodyAsync(context).ConfigureAwait(false);
        var headers = ReadHeaders(context);

        if(!HttpTransportWire.VerifySignature(headers, payload, _hmacSecret)) {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return Results.Accepted();
    }

    private static async Task<byte[]> ReadBodyAsync(HttpContext context) {
        using var buffer = new MemoryStream();
        await context.Request.Body.CopyToAsync(buffer, context.RequestAborted).ConfigureAwait(false);

        return buffer.ToArray();
    }

    private static Dictionary<string, string> ReadHeaders(HttpContext context) {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach(var pair in context.Request.Headers) {
            headers[pair.Key] = pair.Value.ToString();
        }

        return headers;
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action) {
        try {
            await action().ConfigureAwait(false);

            return null;
        }
        catch(Exception exception) {
            return exception;
        }
    }
}