//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Mqtt.Tests;

/// <summary>
/// In-process MQTTnet broker run of the shared contract suite. It gives Docker-less hosts a real
/// protocol round trip (connect, subscribe, QoS 1, acknowledgement) and lets CI debug adapter
/// regressions without pulling a container image; the Mosquitto suite remains the deployment proof.
/// </summary>
[TestClass]
public sealed class MqttLoopbackTransportTests : TransportContractTests {
    private static MqttServer? _server;
    private static int _port;
    private static int _transportCounter;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext) {
        _port = GetFreePort();
        _server = CreateServer(_port);
        await _server.StartAsync().ConfigureAwait(false);
        await MqttBrokerProbe.WaitForPortAsync("127.0.0.1", _port, testContext.CancellationToken).ConfigureAwait(false);
    }

    [ClassCleanup]
    public static async Task ClassCleanupAsync() {
        if(_server is not null) {
            await _server.StopAsync(new MqttServerStopOptionsBuilder().Build()).ConfigureAwait(false);
            _server.Dispose();
        }
    }

    /// <inheritdoc />
    protected override Task<IMessageTransport> CreateTransportAsync(IMessageSerializer serializer, params IMessageTransportHandler[] handlers) {
        var prefix = $"cqs67-loop-{Interlocked.Increment(ref _transportCounter)}-{Guid.NewGuid():N}".ToLowerInvariant();
        var options = MqttTestOptions.Create("127.0.0.1", _port, prefix);

        return Task.FromResult<IMessageTransport>(new MqttTransport(
            Options.Create(options),
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
    public async Task BrokerRestart_ResumesSubscriptionsAsync() {
        // Arrange
        var prefix = $"cqs67-loop-restart-{Guid.NewGuid():N}".ToLowerInvariant();
        var probe = new TransportProbe();
        var transport = new MqttTransport(
            Options.Create(MqttTestOptions.Create("127.0.0.1", _port, prefix)),
            new JsonMessageSerializer(Options.Create(new JsonMessageSerializerOptions())),
            [probe],
            new MqttTopicConvention(prefix));
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            await transport.PublishAsync(new TransportContractMessage("before-restart", 1), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
            await SpinUntilAsync(() => probe.Received.Count >= 1, "initial MQTT delivery", TestContext.CancellationToken).ConfigureAwait(false);

            // Act: replace the broker with a fresh instance on the same port.
            await _server!.StopAsync(new MqttServerStopOptionsBuilder().Build()).ConfigureAwait(false);
            _server.Dispose();
            await SpinUntilAsync(() => !transport.IsConnected, "disconnect detection", TestContext.CancellationToken).ConfigureAwait(false);

            _server = CreateServer(_port);
            await _server.StartAsync().ConfigureAwait(false);
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
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await transport.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task RetainedMessage_IsDeliveredToALateSubscriberAsync() {
        // Arrange
        var prefix = $"cqs67-loop-retained-{Guid.NewGuid():N}".ToLowerInvariant();
        var convention = new MqttTopicConvention(prefix);
        var serializer = new JsonMessageSerializer(Options.Create(new JsonMessageSerializerOptions()));
        var retainedOptions = MqttTestOptions.Create("127.0.0.1", _port, prefix);
        retainedOptions.RetainMessages = true;
        var firstProbe = new TransportProbe();
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
        var late = new MqttTransport(
            Options.Create(MqttTestOptions.Create("127.0.0.1", _port, prefix)),
            serializer,
            [lateProbe],
            convention);
        await late.StartAsync(TestContext.CancellationToken).ConfigureAwait(false);

        try {
            // Act / Assert
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
    public async Task QualityOfServiceLevels_RoundTripMessagesAsync() {
        // Arrange: exercise the 0/1/2 mapping and the acknowledgement path.
        var serializer = new JsonMessageSerializer(Options.Create(new JsonMessageSerializerOptions()));

        foreach(var qualityOfService in new[] { MqttQualityOfService.AtMostOnce, MqttQualityOfService.AtLeastOnce, MqttQualityOfService.ExactlyOnce }) {
            var prefix = $"cqs67-loop-qos{(int)qualityOfService}-{Guid.NewGuid():N}".ToLowerInvariant();
            var probe = new TransportProbe();
            var options = MqttTestOptions.Create("127.0.0.1", _port, prefix);
            options.QualityOfService = qualityOfService;
            var transport = new MqttTransport(Options.Create(options), serializer, [probe], new MqttTopicConvention(prefix));
            await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

            try {
                // Act
                await transport.PublishAsync(new TransportContractMessage($"qos-{(int)qualityOfService}", (int)qualityOfService), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
                await SpinUntilAsync(() => probe.Received.Count >= 1, $"QoS {(int)qualityOfService} delivery", TestContext.CancellationToken).ConfigureAwait(false);

                // Assert
                MSTestAssertions.That(() => probe.Received.Count.Should().Be(1));
            }
            finally {
                await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
                await transport.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static MqttServer CreateServer(int port) =>
        new MqttFactory().CreateMqttServer(
            new MqttServerOptionsBuilder()
                .WithDefaultEndpoint()
                .WithDefaultEndpointPort(port)
                .Build());

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

    private static int GetFreePort() {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }
}