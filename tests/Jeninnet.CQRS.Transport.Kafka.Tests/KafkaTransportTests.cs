//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Kafka.Tests;

/// <summary>
/// Kafka adapter tests: the shared transport contract plus manual-commit redelivery, committed
/// no-redelivery, per-key ordering, DI selection, and options validation. Container tests become
/// inconclusive when Docker is unavailable; CI runs them against a real broker.
/// </summary>
[TestClass]
public sealed class KafkaTransportTests : TransportContractTests {
    private static KafkaContainer? _container;
    private static string? _bootstrapServers;
    private static bool _dockerAvailable;
    private static int _transportCounter;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext) {
        if(!await DockerIsAvailableAsync(testContext.CancellationToken).ConfigureAwait(false)) {
            return;
        }

        _container = new KafkaBuilder("confluentinc/cp-kafka:7.5.12").WithKRaft().Build();
        await _container.StartAsync(testContext.CancellationToken).ConfigureAwait(false);
        _bootstrapServers = new Uri(_container.GetBootstrapAddress()).Authority;
        _dockerAvailable = true;
    }

    [ClassCleanup]
    public static async Task ClassCleanupAsync() {
        if(_container is not null) {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    protected override Task<IMessageTransport> CreateTransportAsync(IMessageSerializer serializer, params IMessageTransportHandler[] handlers) {
        RequireDocker();

        var prefix = UniquePrefix();

        return Task.FromResult<IMessageTransport>(new KafkaTransport(
            Options.Create(CreateOptions(prefix)),
            serializer,
            handlers,
            new TransportNamingConvention(prefix)));
    }

    [TestMethod]
    public async Task UncommittedHandlerFailure_IsRedeliveredOnRestartAsync() {
        // Arrange
        RequireDocker();
        var prefix = UniquePrefix();
        var options = CreateOptions(prefix);
        var namingConvention = new TransportNamingConvention(prefix);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IMessageTransport first = new KafkaTransport(Options.Create(options), CreateSerializer(), [new FailingHandler(failed)], namingConvention);
        await first.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            // Act: the handler fails, so the offset is never committed.
            await first.PublishAsync(new TransportContractMessage("retry-me", 1), new TransportMessageOptions { PartitionKey = "key" }, TestContext.CancellationToken).ConfigureAwait(false);
            await failed.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.CancellationToken).ConfigureAwait(false);
        }
        finally {
            await first.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        // A fresh transport in the same group starts from the last committed offset (none).
        var probe = new TransportProbe();
        var second = new KafkaTransport(Options.Create(options), CreateSerializer(), [probe], namingConvention);
        await second.StartAsync(TestContext.CancellationToken).ConfigureAwait(false);

        try {
            await SpinUntilAsync(() => probe.Received.Count >= 1, "redelivery after restart", TestContext.CancellationToken).ConfigureAwait(false);
            var body = (TransportContractMessage?)CreateSerializer().Deserialize(probe.Received[0].Payload.Span, typeof(TransportContractMessage));

            // Assert
            MSTestAssertions.That(() => body.Should().Be(new TransportContractMessage("retry-me", 1)));
        }
        finally {
            await second.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task CommittedMessage_IsNotRedeliveredAfterRestartAsync() {
        // Arrange
        RequireDocker();
        var prefix = UniquePrefix();
        var options = CreateOptions(prefix);
        var namingConvention = new TransportNamingConvention(prefix);
        var firstProbe = new TransportProbe();
        IMessageTransport first = new KafkaTransport(Options.Create(options), CreateSerializer(), [firstProbe], namingConvention);
        await first.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            await first.PublishAsync(new TransportContractMessage("once", 1), new TransportMessageOptions { PartitionKey = "key" }, TestContext.CancellationToken).ConfigureAwait(false);
            await SpinUntilAsync(() => firstProbe.Received.Count >= 1, "first delivery", TestContext.CancellationToken).ConfigureAwait(false);

            // Give the commit that follows the handler a moment to reach the group coordinator.
            await Task.Delay(500, TestContext.CancellationToken).ConfigureAwait(false);
        }
        finally {
            await first.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        // Act: same group/topic, new transport; the committed offset must prevent redelivery.
        var secondProbe = new TransportProbe();
        var second = new KafkaTransport(Options.Create(options), CreateSerializer(), [secondProbe], namingConvention);
        await second.StartAsync(TestContext.CancellationToken).ConfigureAwait(false);

        try {
            await Task.Delay(2_000, TestContext.CancellationToken).ConfigureAwait(false);

            // Assert
            MSTestAssertions.That(() => secondProbe.Received.Should().BeEmpty());
        }
        finally {
            await second.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task SamePartitionKey_PreservesOrderAsync() {
        // Arrange
        RequireDocker();
        var probe = new TransportProbe();
        var transport = await CreateTransportAsync(CreateSerializer(), probe).ConfigureAwait(false);
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        const int count = 20;

        try {
            // Act: one key maps to one partition; the single consumer processes in order.
            for(var index = 0; index < count; index++) {
                await transport.PublishAsync(
                    new TransportContractMessage($"m{index}", index),
                    new TransportMessageOptions { PartitionKey = "ordered-key" },
                    TestContext.CancellationToken).ConfigureAwait(false);
            }

            await SpinUntilAsync(() => probe.Received.Count >= count, "ordered deliveries", TestContext.CancellationToken).ConfigureAwait(false);

            // Assert
            var names = probe.Received
                .Select(envelope => (TransportContractMessage?)CreateSerializer().Deserialize(envelope.Payload.Span, typeof(TransportContractMessage)))
                .Select(message => message!.Name)
                .ToList();
            MSTestAssertions.That(() => names.Should().Equal(Enumerable.Range(0, count).Select(index => $"m{index}")));
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public void AddCqrsKafka_RegistersTheTransportAndSelectsExternalMode() {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCqrsKafka(static options => options.Subscriptions.Add(new KafkaSubscription { MessageType = typeof(TransportContractMessage) }));
        using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IMessageTransport>();
        var mode = provider.GetRequiredService<IOptions<CqrsTransportOptions>>().Value.Mode;
        var hostedServices = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().ToList();

        // Assert
        MSTestAssertions.That(() => transport.Should().BeOfType<KafkaTransport>());
        MSTestAssertions.That(() => mode.Should().Be(CqrsTransportMode.External));
        MSTestAssertions.That(() => hostedServices.Should().NotBeEmpty("the transport must start and stop with the host"));
    }

    [TestMethod]
    public void InvalidBootstrapServers_IsRejectedWhenTheTransportIsConstructed() {
        // Arrange
        var options = new KafkaTransportOptions { BootstrapServers = string.Empty };

        // Act
        var thrown = Capture(() => _ = new KafkaTransport(
            Options.Create(options),
            CreateSerializer(),
            [],
            new TransportNamingConvention()));

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<ArgumentException>());
    }

    private static KafkaTransportOptions CreateOptions(string prefix) {
        var options = new KafkaTransportOptions {
            BootstrapServers = _bootstrapServers ?? throw new InvalidOperationException("The Kafka container is not available."),
            ConsumerGroup = prefix,
            ClientId = "jeninnet-cqrs-kafka-tests",
            NumPartitions = 3,
            HandlerFailureDelay = TimeSpan.FromMilliseconds(50),
        };
        options.Subscriptions.Add(new KafkaSubscription { MessageType = typeof(TransportContractMessage) });

        return options;
    }

    private static string UniquePrefix() =>
        $"cqrs43-{Interlocked.Increment(ref _transportCounter)}-{Guid.NewGuid():N}".ToLowerInvariant();

    private static JsonMessageSerializer CreateSerializer() =>
        new(Options.Create(new JsonMessageSerializerOptions()));

    private static void RequireDocker() {
        if(!_dockerAvailable || _container is null) {
            Assert.Inconclusive("Docker is not available; the Kafka integration tests were skipped.");
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

    private sealed class FailingHandler(TaskCompletionSource failed) : IMessageTransportHandler {
        public Task HandleAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default) {
            failed.TrySetResult();

            throw new InvalidOperationException("poison message");
        }
    }
}