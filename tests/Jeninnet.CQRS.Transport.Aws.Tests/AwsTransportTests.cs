//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//

namespace Jeninnet.CQRS.Transport.Aws.Tests;

/// <summary>
/// AWS adapter tests: shared contract, claim-check handoff at the SQS payload cap, visibility
/// extension, FIFO ordering, DI/mode, and options validation. Container tests become inconclusive
/// when Docker is unavailable; CI runs them against LocalStack.
/// </summary>
[TestClass]
public sealed class AwsTransportTests : TransportContractTests {
    private static LocalStackContainer? _container;
    private static string? _serviceUrl;
    private static bool _dockerAvailable;
    private static string? _oldAccessKey;
    private static string? _oldSecretKey;
    private static string? _oldRegion;
    private static int _transportCounter;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext) {
        if(!await DockerIsAvailableAsync(testContext.CancellationToken).ConfigureAwait(false)) {
            return;
        }

        _oldAccessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
        _oldSecretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
        _oldRegion = Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION");
        Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", "test");
        Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "test");
        Environment.SetEnvironmentVariable("AWS_DEFAULT_REGION", "us-east-1");

        _container = new LocalStackBuilder("localstack/localstack:4.14")
            .WithEnvironment("SERVICES", "sqs,sns")
            .Build();
        await _container.StartAsync(testContext.CancellationToken).ConfigureAwait(false);
        _serviceUrl = _container.GetConnectionString();
        _dockerAvailable = true;
    }

    [ClassCleanup]
    public static async Task ClassCleanupAsync() {
        if(_container is not null) {
            await _container.DisposeAsync().ConfigureAwait(false);
        }

        Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", _oldAccessKey);
        Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", _oldSecretKey);
        Environment.SetEnvironmentVariable("AWS_DEFAULT_REGION", _oldRegion);
    }

    /// <inheritdoc />
    protected override Task<IMessageTransport> CreateTransportAsync(IMessageSerializer serializer, params IMessageTransportHandler[] handlers) {
        RequireDocker();

        var prefix = UniquePrefix();

        return Task.FromResult<IMessageTransport>(new AwsTransport(
            Options.Create(CreateOptions(prefix)),
            serializer,
            handlers,
            new TransportNamingConvention(prefix),
            CreateClaimChecks()));
    }

    [TestMethod]
    public async Task LargePayload_IsStoredInClaimCheckAndDeliveredIntactAsync() {
        // Arrange
        RequireDocker();
        var prefix = UniquePrefix();
        var probe = new TransportProbe();
        var claimChecks = CreateClaimChecks();
        IMessageTransport transport = new AwsTransport(
            Options.Create(CreateOptions(prefix)),
            CreateSerializer(),
            [probe],
            new TransportNamingConvention(prefix),
            claimChecks);
        var largeName = new string('x', 300_000);
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            // Act: the serialized payload exceeds the claim-check threshold, so SQS carries only a reference.
            await transport.SendAsync(new TransportContractMessage(largeName, 1), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
            await SpinUntilAsync(() => probe.Received.Count >= 1, "claim-check delivery", TestContext.CancellationToken).ConfigureAwait(false);

            // Assert
            var delivered = (TransportContractMessage?)CreateSerializer().Deserialize(probe.Received[0].Payload.Span, typeof(TransportContractMessage));
            MSTestAssertions.That(() => delivered.Should().NotBeNull());
            MSTestAssertions.That(() => delivered!.Name.Length.Should().Be(largeName.Length));
            MSTestAssertions.That(() => delivered!.Name.Should().Be(largeName));
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task VisibilityRenewal_KeepsALongHandlerInvisibleToAnotherReceiverAsync() {
        // Arrange
        RequireDocker();
        var prefix = UniquePrefix();
        var options = CreateOptions(prefix);
        options.VisibilityTimeout = TimeSpan.FromSeconds(1);
        options.VisibilityRenewalInterval = TimeSpan.FromMilliseconds(250);
        var claimChecks = CreateClaimChecks();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executions = 0;
        IMessageTransport transport = new SqsTransport(
            Options.Create(options),
            CreateSerializer(),
            [new DelegateHandler(async (_, _) => {
                Interlocked.Increment(ref executions);
                started.TrySetResult();
                await Task.Delay(3_000, CancellationToken.None).ConfigureAwait(false);
            })],
            new TransportNamingConvention(prefix),
            claimChecks);
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            await transport.SendAsync(new TransportContractMessage("long", 1), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken).ConfigureAwait(false);
            await Task.Delay(1_500, TestContext.CancellationToken).ConfigureAwait(false);

            // Act: a competing receiver must not see the message while the handler runs.
            using var competing = new AmazonSQSClient(new AmazonSQSConfig {
                RegionEndpoint = RegionEndpoint.USEast1,
                ServiceURL = _serviceUrl,
            });
            var queueUrl = (await competing.GetQueueUrlAsync(SqsQueueName(prefix), TestContext.CancellationToken).ConfigureAwait(false)).QueueUrl;
            var received = await competing.ReceiveMessageAsync(new Amazon.SQS.Model.ReceiveMessageRequest {
                QueueUrl = queueUrl,
                WaitTimeSeconds = 0,
                VisibilityTimeout = 1,
            }, TestContext.CancellationToken).ConfigureAwait(false);

            await Task.Delay(2_500, TestContext.CancellationToken).ConfigureAwait(false);

            // Assert
            MSTestAssertions.That(() => (received.Messages?.Count ?? 0).Should().Be(0, "visibility renewal keeps the lease alive"));
            MSTestAssertions.That(() => executions.Should().Be(1));
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task Fifo_PreservesOrderPerPartitionKeyAsync() {
        // Arrange
        RequireDocker();
        var prefix = UniquePrefix();
        var options = CreateOptions(prefix);
        options.UseFifo = true;
        var probe = new TransportProbe();
        IMessageTransport transport = new AwsTransport(
            Options.Create(options),
            CreateSerializer(),
            [probe],
            new TransportNamingConvention(prefix),
            CreateClaimChecks());
        const int count = 10;
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            // Act
            for(var index = 0; index < count; index++) {
                await transport.PublishAsync(
                    new TransportContractMessage($"f{index}", index),
                    new TransportMessageOptions { PartitionKey = "fifo-key" },
                    TestContext.CancellationToken).ConfigureAwait(false);
            }

            await SpinUntilAsync(() => probe.Received.Count >= count, "FIFO deliveries", TestContext.CancellationToken).ConfigureAwait(false);

            // Assert
            var names = probe.Received
                .Select(envelope => (TransportContractMessage?)CreateSerializer().Deserialize(envelope.Payload.Span, typeof(TransportContractMessage)))
                .Select(message => message!.Name)
                .ToList();
            MSTestAssertions.That(() => names.Should().Equal(Enumerable.Range(0, count).Select(index => $"f{index}")));
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public void AddCqrsAws_RegistersTheTransportAndSelectsExternalMode() {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCqrsAws(static options => options.Subscriptions.Add(new AwsSubscription { MessageType = typeof(TransportContractMessage) }));
        using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IMessageTransport>();
        var mode = provider.GetRequiredService<IOptions<CqrsTransportOptions>>().Value.Mode;
        var hostedServices = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().ToList();

        // Assert
        MSTestAssertions.That(() => transport.Should().BeOfType<AwsTransport>());
        MSTestAssertions.That(() => mode.Should().Be(CqrsTransportMode.External));
        MSTestAssertions.That(() => hostedServices.Should().NotBeEmpty("the transport must start and stop with the host"));
    }

    [TestMethod]
    public void InvalidVisibilityRenewal_IsRejectedWhenTheTransportIsConstructed() {
        // Arrange
        var options = new AwsTransportOptions {
            VisibilityTimeout = TimeSpan.FromSeconds(1),
            VisibilityRenewalInterval = TimeSpan.FromSeconds(1),
        };

        // Act
        var thrown = Capture(() => _ = new AwsTransport(
            Options.Create(options),
            CreateSerializer(),
            [],
            new TransportNamingConvention(),
            CreateClaimChecks()));

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<ArgumentOutOfRangeException>());
    }

    private static AwsTransportOptions CreateOptions(string prefix) {
        var options = new AwsTransportOptions {
            Region = "us-east-1",
            ServiceUrl = _serviceUrl ?? throw new InvalidOperationException("The LocalStack container is not available."),
            EnsureTopology = true,
            VisibilityTimeout = TimeSpan.FromSeconds(2),
            VisibilityRenewalInterval = TimeSpan.FromMilliseconds(500),
            ReceiveWaitSeconds = 1,
            MaxNumberOfMessages = 10,
            PayloadLimitBytes = 240 * 1024,
        };
        options.Subscriptions.Add(new AwsSubscription { MessageType = typeof(TransportContractMessage) });

        return options;
    }

    private static ClaimCheckPolicy CreateClaimChecks() =>
        new(Options.Create(new ClaimCheckOptions()), [new InMemoryClaimCheckStore()]);

    private static string SqsQueueName(string prefix) {
        var raw = new TransportNamingConvention(prefix).Describe(typeof(TransportContractMessage)).Queue;
        var builder = new StringBuilder(raw.Length);

        foreach(var character in raw) {
            builder.Append(character is >= 'a' and <= 'z' ||
                           character is >= 'A' and <= 'Z' ||
                           character is >= '0' and <= '9' ||
                           character is '-' or '_'
                ? character
                : '-');
        }

        return builder.ToString();
    }

    private static string UniquePrefix() =>
        $"t{Interlocked.Increment(ref _transportCounter)}";

    private static JsonMessageSerializer CreateSerializer() =>
        new(Options.Create(new JsonMessageSerializerOptions()));

    private static void RequireDocker() {
        if(!_dockerAvailable || _container is null) {
            Assert.Inconclusive("Docker is not available; the AWS integration tests were skipped.");
        }
    }

    private static Task<bool> DockerIsAvailableAsync(CancellationToken cancellationToken) =>
        DockerAvailability.IsAvailableAsync(cancellationToken);

    private static async Task SpinUntilAsync(Func<bool> condition, string what, CancellationToken cancellationToken, int timeoutMilliseconds = 30_000) {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;

        while(!condition()) {
            if(Environment.TickCount64 > deadline) {
                break;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        MSTestAssertions.That(() => condition().Should().BeTrue($"timed out waiting for: {what}"));
    }

    private static Exception? Capture(Action action) {
        try {
            action();

            return null;
        }
        catch(Exception exception) {
            return exception;
        }
    }

    private sealed class DelegateHandler(Func<TransportEnvelope, CancellationToken, Task> handle) : IMessageTransportHandler {
        public Task HandleAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default) =>
            handle(envelope, cancellationToken);
    }
}