//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.SignalR.Tests;

/// <summary>
/// SignalR adapter tests: the shared transport contract against an in-memory hub, reconnect after
/// a real Kestrel hub restart, and client registration. Long polling is used against TestServer
/// because its in-memory handler does not own a WebSocket listener; Kestrel reconnection uses the
/// default transport negotiation.
/// </summary>
[TestClass]
public sealed class SignalRTransportTests : TransportContractTests {
    private static WebApplication? _app;
    private static TestServer? _server;
    private static int _counter;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext) {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSignalR();

        _app = builder.Build();
        _app.MapCqrsSignalRHub("/cqrs");

        await _app.StartAsync(testContext.CancellationToken).ConfigureAwait(false);
        _server = _app.GetTestServer();
    }

    [ClassCleanup]
    public static async Task ClassCleanupAsync() {
        if(_app is not null) {
            await _app.StopAsync().ConfigureAwait(false);
            await _app.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    protected override Task<IMessageTransport> CreateTransportAsync(IMessageSerializer serializer, params IMessageTransportHandler[] handlers) {
        var id = Interlocked.Increment(ref _counter).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var options = CreateOptions("http://localhost/cqrs");
        options.ConfigureConnection = connection => {
            connection.Transports = HttpTransportType.LongPolling;
            connection.HttpMessageHandlerFactory = _ => _server!.CreateHandler();
        };

        return Task.FromResult<IMessageTransport>(new SignalRTransport(
            Options.Create(options),
            serializer,
            handlers,
            new TransportNamingConvention($"cqrs.signalr.{id}")));
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeTransportAsync(IMessageTransport transport) {
        if(transport is IAsyncDisposable disposable) {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task Reconnect_ResumesDeliveryAfterHubRestartAsync() {
        // Arrange
        if(OperatingSystem.IsWindows()) {
            Assert.Inconclusive("Restarting the in-process Kestrel hub is not reliable on Windows; the Linux CI runner covers this scenario.");
        }

        var probe = new TransportProbe();
        var serializer = CreateSerializer();
        var (firstApp, port) = await StartServerAsync(0).ConfigureAwait(false);
        var options = CreateOptions($"http://127.0.0.1:{port}/cqrs");
        await using var transport = new SignalRTransport(Options.Create(options), serializer, [probe], new TransportNamingConvention("cqrs.signalr.reconnect"));

        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            await transport.PublishAsync(new TransportContractMessage("before-restart", 1), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
            await WaitForAsync(() => probe.Received.Count >= 1, "initial SignalR delivery").ConfigureAwait(false);

            // Act: stop the hub, then bring a new host up on the same port.
            await StopServerAsync(firstApp).ConfigureAwait(false);
            await WaitForAsync(() => transport.ConnectionState is not HubConnectionState.Connected, "disconnect detection").ConfigureAwait(false);

            var (secondApp, _) = await StartServerAsync(port).ConfigureAwait(false);

            try {
                await WaitForAsync(
                    () => transport.ConnectionState is HubConnectionState.Connected,
                    "automatic reconnect",
                    TimeSpan.FromSeconds(30)).ConfigureAwait(false);

                await transport.PublishAsync(new TransportContractMessage("after-restart", 2), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
                await WaitForAsync(() => probe.Received.Count >= 2, "SignalR delivery after reconnect", TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
            finally {
                await StopServerAsync(secondApp).ConfigureAwait(false);
            }

            // Assert
            MSTestAssertions.That(() => probe.Received.Count.Should().Be(2));
            MSTestAssertions.That(() => probe.Received[1].Headers[TransportHeaders.MessageVersion].Should().Be("1"));
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public void AddCqrsSignalR_RegistersTheTransportAndSelectsExternalMode() {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCqrsSignalR(options => options.HubUrl = "http://localhost/cqrs");
        using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IMessageTransport>();
        var mode = provider.GetRequiredService<IOptions<CqrsTransportOptions>>().Value.Mode;

        // Assert
        MSTestAssertions.That(() => transport.Should().BeOfType<SignalRTransport>());
        MSTestAssertions.That(() => mode.Should().Be(CqrsTransportMode.External));
    }

    private static SignalRTransportOptions CreateOptions(string hubUrl) {
        var options = new SignalRTransportOptions { HubUrl = hubUrl };
        options.ReconnectDelays.Clear();
        options.ReconnectDelays.Add(TimeSpan.Zero);
        options.ReconnectDelays.Add(TimeSpan.FromMilliseconds(100));
        options.ReconnectDelays.Add(TimeSpan.FromMilliseconds(250));
        options.ReconnectDelays.Add(TimeSpan.FromMilliseconds(500));

        return options;
    }

    private static JsonMessageSerializer CreateSerializer() =>
        new(Options.Create(new JsonMessageSerializerOptions()));

    private static async Task<(WebApplication App, int Port)> StartServerAsync(int port, CancellationToken cancellationToken = default) {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Services.AddSignalR();

        var app = builder.Build();
        app.MapCqrsSignalRHub("/cqrs");
        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        var address = app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()!
            .Addresses
            .Single();

        return (app, new Uri(address).Port);
    }

    private static async Task StopServerAsync(WebApplication app) {
        await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
        await app.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task WaitForAsync(Func<bool> condition, string what, TimeSpan? timeout = null) {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));

        while(!condition()) {
            if(DateTime.UtcNow > deadline) {
                break;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        MSTestAssertions.That(() => condition().Should().BeTrue($"timed out waiting for: {what}"));
    }
}
