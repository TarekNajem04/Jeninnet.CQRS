//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Mqtt.Tests;

/// <summary>
/// MQTT adapter tests: the shared transport contract against a Mosquitto container, tenant topic
/// formatting, retained delivery, broker-restart reconnection, and DI selection. Container tests
/// become inconclusive when Docker is unavailable; CI runs them against a real broker.
/// </summary>
[TestClass]
public sealed class MqttTransportTests : TransportContractTests {
    private static MosquittoContainer? _container;
    private static string _host = "localhost";
    private static int _port = 1883;
    private static bool _dockerAvailable;
    private static int _transportCounter;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext) {
        if(!await DockerIsAvailableAsync(testContext.CancellationToken).ConfigureAwait(false)) {
            return;
        }

        // Pin the host port: Testcontainers remaps a random host port when the same container is
        // stopped and started again, and the reconnect test needs a stable endpoint.
        var hostPort = MqttBrokerProbe.GetFreePort();
        _container = new MosquittoBuilder("eclipse-mosquitto:2.0")
            .WithPortBinding(hostPort, 1883)
            .Build();
        await _container.StartAsync(testContext.CancellationToken).ConfigureAwait(false);

        _host = "127.0.0.1";
        _port = hostPort;

        // Testcontainers reports the container as started before Mosquitto binds its listener.
        await MqttBrokerProbe.WaitForPortAsync(_host, _port, testContext.CancellationToken).ConfigureAwait(false);

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

        return Task.FromResult<IMessageTransport>(new MqttTransport(
            Options.Create(CreateOptions(prefix)),
            serializer,
            handlers,
            new MqttTopicConvention(prefix)));
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeTransportAsync(IMessageTransport transport) {
        if(transport is IAsyncDisposable disposable) {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public void TopicConvention_UsesPrefixTypeAndOptionalTenantLevels() {
        // Arrange
        var convention = new MqttTopicConvention();
        var type = typeof(TransportContractMessage);

        // Act
        var tenantless = convention.Describe(type);
        var tenant = convention.Describe(type, "tenant-1");
        var filters = convention.GetSubscriptionFilters(type);

        // Assert
        MSTestAssertions.That(() => tenantless.Should().Be($"cqrs/{type.FullName}"));
        MSTestAssertions.That(() => tenant.Should().Be($"cqrs/{type.FullName}/tenant-1"));
        MSTestAssertions.That(() => filters.Should().HaveCount(2));
        MSTestAssertions.That(() => filters[0].Should().Be(tenantless));
        MSTestAssertions.That(() => filters[1].Should().Be($"{tenantless}/+"));
    }

    [TestMethod]
    public void TopicConvention_SanitizesWildcardsAndSeparators() {
        // Arrange
        var convention = new MqttTopicConvention("cqrs/edge");
        var type = typeof(TransportContractMessage);

        // Act
        var topic = convention.Describe(type, "tenant/#/one");
        var tenantFound = convention.TryGetTenant(topic, type, out var tenant);

        // Assert
        MSTestAssertions.That(() => convention.Prefix.Should().Be("cqrs-edge"));
        MSTestAssertions.That(() => topic.Should().Be($"cqrs-edge/{type.FullName}/tenant---one"));
        MSTestAssertions.That(() => tenantFound.Should().BeTrue());
        MSTestAssertions.That(() => tenant.Should().Be("tenant---one"));
    }

    [TestMethod]
    public async Task Publish_CarriesTenantInTheTopicAndEnvelopeAsync() {
        // Arrange
        RequireDocker();
        var prefix = UniquePrefix();
        var convention = new MqttTopicConvention(prefix);
        var probe = new TransportProbe();
        var transport = new MqttTransport(
            Options.Create(CreateOptions(prefix)),
            CreateSerializer(),
            [probe],
            convention);
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            // Act
            await transport.PublishAsync(
                new TransportContractMessage("tenant", 1),
                new TransportMessageOptions { TenantId = "tenant-1" },
                TestContext.CancellationToken).ConfigureAwait(false);
            await SpinUntilAsync(() => probe.Received.Count >= 1, "tenant delivery", TestContext.CancellationToken).ConfigureAwait(false);

            // Assert
            var envelope = probe.Received.Single();
            MSTestAssertions.That(() => envelope.TenantId.Should().Be("tenant-1"));
            MSTestAssertions.That(() => envelope.Topology.Topic.Should().Be(convention.Describe(typeof(TransportContractMessage), "tenant-1")));
            MSTestAssertions.That(() => envelope.Headers[TransportHeaders.Tenant].Should().Be("tenant-1"));
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await transport.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task RetainedMessage_IsDeliveredToALateSubscriberAsync() {
        // Arrange
        RequireDocker();
        var prefix = UniquePrefix();
        var convention = new MqttTopicConvention(prefix);
        var serializer = CreateSerializer();
        var firstProbe = new TransportProbe();
        var retainedOptions = CreateOptions(prefix);
        retainedOptions.RetainMessages = true;
        var first = new MqttTransport(Options.Create(retainedOptions), serializer, [firstProbe], convention);
        await first.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            await first.PublishAsync(new TransportContractMessage("retained", 7), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
            await SpinUntilAsync(() => firstProbe.Received.Count >= 1, "initial retained publish", TestContext.CancellationToken).ConfigureAwait(false);
        }
        finally {
            await first.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await first.DisposeAsync().ConfigureAwait(false);
        }

        var lateProbe = new TransportProbe();
        var late = new MqttTransport(Options.Create(CreateOptions(prefix)), serializer, [lateProbe], convention);
        await late.StartAsync(TestContext.CancellationToken).ConfigureAwait(false);

        try {
            // Act / Assert: the broker sends the retained message when the new client subscribes.
            await SpinUntilAsync(() => lateProbe.Received.Count >= 1, "retained delivery on subscribe", TestContext.CancellationToken).ConfigureAwait(false);
            var body = (TransportContractMessage?)serializer.Deserialize(lateProbe.Received[0].Payload.Span, typeof(TransportContractMessage));
            MSTestAssertions.That(() => body.Should().Be(new TransportContractMessage("retained", 7)));
        }
        finally {
            await late.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await late.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task BrokerRestart_ResumesSubscriptionsAsync() {
        // Arrange
        RequireDocker();

        if(OperatingSystem.IsWindows()) {
            Assert.Inconclusive("Restarting a Testcontainers Mosquitto container is not reliable on Docker Desktop for Windows; the Linux CI runner covers this scenario.");
        }

        var prefix = UniquePrefix();
        var probe = new TransportProbe();
        var transport = new MqttTransport(
            Options.Create(CreateOptions(prefix)),
            CreateSerializer(),
            [probe],
            new MqttTopicConvention(prefix));
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            await transport.PublishAsync(new TransportContractMessage("before-restart", 1), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
            await SpinUntilAsync(() => probe.Received.Count >= 1, "initial MQTT delivery", TestContext.CancellationToken).ConfigureAwait(false);

            // Act: stop the broker process, then start the same container again.
            await _container!.StopAsync(TestContext.CancellationToken).ConfigureAwait(false);
            await SpinUntilAsync(() => !transport.IsConnected, "disconnect detection", TestContext.CancellationToken).ConfigureAwait(false);
            await _container.StartAsync(TestContext.CancellationToken).ConfigureAwait(false);
            await MqttBrokerProbe.WaitForPortAsync(_host, _port, TestContext.CancellationToken).ConfigureAwait(false);

            await SpinUntilAsync(() => transport.IsConnected, "automatic reconnect", TestContext.CancellationToken).ConfigureAwait(false);

            var deadline = Environment.TickCount64 + 30_000;

            while(Environment.TickCount64 < deadline) {
                try {
                    await transport.PublishAsync(new TransportContractMessage("after-restart", 2), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

                    break;
                }
                catch(InvalidOperationException) {
                    await Task.Delay(100, TestContext.CancellationToken).ConfigureAwait(false);
                }
            }

            await SpinUntilAsync(() => probe.Received.Count >= 2, "delivery after broker restart", TestContext.CancellationToken).ConfigureAwait(false);

            // Assert
            MSTestAssertions.That(() => probe.Received.Count.Should().Be(2));
            MSTestAssertions.That(() => probe.Received[1].Headers[TransportHeaders.MessageVersion].Should().Be("1"));
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await transport.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public void AddCqrsMqtt_RegistersTheTransportAndSelectsExternalMode() {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCqrsMqtt(static options => options.Subscriptions.Add(new MqttSubscription { MessageType = typeof(TransportContractMessage) }));
        using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IMessageTransport>();
        var mode = provider.GetRequiredService<IOptions<CqrsTransportOptions>>().Value.Mode;

        // Assert
        MSTestAssertions.That(() => transport.Should().BeOfType<MqttTransport>());
        MSTestAssertions.That(() => mode.Should().Be(CqrsTransportMode.External));
    }

    [TestMethod]
    public void CleanSessionWithoutClientId_IsRejected() {
        // Act
        MqttTransport? transport = null;
        var thrown = Capture(() => transport = new MqttTransport(
            Options.Create(new MqttTransportOptions { CleanSession = false }),
            CreateSerializer(),
            [],
            new MqttTopicConvention()));

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<ArgumentException>());
        transport?.Dispose();
    }

    private static MqttTransportOptions CreateOptions(string prefix) {
        var options = new MqttTransportOptions {
            HostName = _host,
            Port = _port,
            ClientId = $"{prefix}-client",
            CleanSession = true,
            KeepAlivePeriod = TimeSpan.FromSeconds(5),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ReconnectDelay = TimeSpan.FromMilliseconds(100),
            MaxReconnectDelay = TimeSpan.FromMilliseconds(500),
            QualityOfService = MqttQualityOfService.AtLeastOnce,
        };

        options.Subscriptions.Add(new MqttSubscription { MessageType = typeof(TransportContractMessage) });

        return options;
    }

    private static string UniquePrefix() =>
        $"cqs67-{Interlocked.Increment(ref _transportCounter)}-{Guid.NewGuid():N}".ToLowerInvariant();

    private static JsonMessageSerializer CreateSerializer() =>
        new(Options.Create(new JsonMessageSerializerOptions()));

    private static void RequireDocker() {
        if(!_dockerAvailable || _container is null) {
            Assert.Inconclusive("Docker is not available; the MQTT integration tests were skipped.");
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
}
