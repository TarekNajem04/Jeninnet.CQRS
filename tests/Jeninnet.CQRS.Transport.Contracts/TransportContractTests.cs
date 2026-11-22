//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Contracts;

/// <summary>
/// Reusable contract every <see cref="IMessageTransport"/> must satisfy. Broker packages derive
/// from this class in their own test project; the transport is created unstarted and the suite
/// owns start/stop/dispose.
/// </summary>
/// <remarks>
/// The suite asserts the transport-neutral promises only: envelope metadata and payload survive,
/// send reaches one consumer, publish reaches every consumer, and a stopped transport rejects
/// new messages. Broker-specific behavior (confirms, reconnects, offsets) belongs to the broker
/// package's own tests.
/// </remarks>
[TestClass]
public abstract class TransportContractTests {
    /// <summary>Gets or sets the MSTest context (used for cancellation).</summary>
    public TestContext? TestContext { get; set; }

    /// <summary>Creates an unstarted transport wired to <paramref name="handlers"/>.</summary>
    /// <param name="serializer">The serializer the transport must use.</param>
    /// <param name="handlers">The consumer-side handlers registered for this transport.</param>
    protected abstract Task<IMessageTransport> CreateTransportAsync(IMessageSerializer serializer, params IMessageTransportHandler[] handlers);

    /// <summary>Releases broker resources after the suite stops the transport.</summary>
    /// <param name="transport">The transport created by <see cref="CreateTransportAsync"/>.</param>
    protected virtual ValueTask DisposeTransportAsync(IMessageTransport transport) =>
        ValueTask.CompletedTask;

    /// <summary>Envelope metadata and payload survive a send round trip.</summary>
    [TestMethod]
    public async Task Send_RoundTripsTheMessageAndPreservesEnvelopeFieldsAsync() {
        // Arrange
        var serializer = CreateSerializer();
        var probe = new TransportProbe();
        var message = new TransportContractMessage("hello", 42);
        var options = new TransportMessageOptions {
            MessageId = "message-1",
            CorrelationId = "correlation-1",
            CausationId = "causation-1",
            TenantId = "tenant-1",
            PartitionKey = "partition-1",
            MessageVersion = 2,
            Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["trace"] = "abc-123" },
        };

        // Act
        await RunAsync(serializer, [probe], async (transport, cancellationToken) => {
            await transport.SendAsync(message, options, cancellationToken).ConfigureAwait(false);
            await WaitForAsync(() => probe.Received.Count >= 1, "send delivery").ConfigureAwait(false);
        }).ConfigureAwait(false);

        // Assert
        var envelope = probe.Received.Single();
        var roundTripped = (TransportContractMessage?)serializer.Deserialize(envelope.Payload.Span, typeof(TransportContractMessage));

        MSTestAssertions.That(() => envelope.MessageId.Should().Be("message-1"));
        MSTestAssertions.That(() => envelope.CorrelationId.Should().Be("correlation-1"));
        MSTestAssertions.That(() => envelope.CausationId.Should().Be("causation-1"));
        MSTestAssertions.That(() => envelope.TenantId.Should().Be("tenant-1"));
        MSTestAssertions.That(() => envelope.PartitionKey.Should().Be("partition-1"));
        MSTestAssertions.That(() => envelope.Headers["trace"].Should().Be("abc-123"));
        MSTestAssertions.That(() => envelope.MessageVersion.Should().Be(2));
        MSTestAssertions.That(() => envelope.Headers[TransportHeaders.MessageVersion].Should().Be("2"));
        MSTestAssertions.That(() => envelope.Headers[TransportHeaders.MessageType].Should().Be(envelope.MessageType));
        MSTestAssertions.That(() => envelope.Headers[TransportHeaders.ContentType].Should().Be(serializer.ContentType));
        MSTestAssertions.That(() => string.IsNullOrWhiteSpace(envelope.ContentType).Should().BeFalse());
        MSTestAssertions.That(() => (envelope.Timestamp == default(DateTimeOffset)).Should().BeFalse());
        MSTestAssertions.That(() => envelope.DeliveryMode.Should().Be(TransportDeliveryMode.Send));
        MSTestAssertions.That(() => envelope.MessageType.Should().Contain(nameof(TransportContractMessage)));
        MSTestAssertions.That(() => string.IsNullOrWhiteSpace(envelope.Topology.Exchange).Should().BeFalse());
        MSTestAssertions.That(() => string.IsNullOrWhiteSpace(envelope.Topology.Queue).Should().BeFalse());
        MSTestAssertions.That(() => string.IsNullOrWhiteSpace(envelope.Topology.Topic).Should().BeFalse());
        MSTestAssertions.That(() => string.IsNullOrWhiteSpace(envelope.Topology.RoutingKey).Should().BeFalse());
        MSTestAssertions.That(() => roundTripped.Should().Be(message));
    }

    /// <summary>Publish fans out to every registered consumer.</summary>
    [TestMethod]
    public async Task Publish_ReachesEveryConsumerAsync() {
        // Arrange
        var serializer = CreateSerializer();
        var first = new TransportProbe();
        var second = new TransportProbe();

        // Act
        await RunAsync(serializer, [first, second], async (transport, cancellationToken) => {
            await transport.PublishAsync(new TransportContractMessage("fan-out", 7), cancellationToken: cancellationToken).ConfigureAwait(false);
            await WaitForAsync(() => first.Received.Count >= 1 && second.Received.Count >= 1, "publish fan-out").ConfigureAwait(false);
        }).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => first.Received.Should().HaveCount(1));
        MSTestAssertions.That(() => second.Received.Should().HaveCount(1));
        MSTestAssertions.That(() => first.Received[0].DeliveryMode.Should().Be(TransportDeliveryMode.Publish));
        MSTestAssertions.That(() => second.Received[0].DeliveryMode.Should().Be(TransportDeliveryMode.Publish));
    }

    /// <summary>Send is point-to-point: exactly one consumer receives it.</summary>
    [TestMethod]
    public async Task Send_ReachesOnlyOneConsumerAsync() {
        // Arrange
        var serializer = CreateSerializer();
        var first = new TransportProbe();
        var second = new TransportProbe();

        // Act
        await RunAsync(serializer, [first, second], async (transport, cancellationToken) => {
            await transport.SendAsync(new TransportContractMessage("one", 1), cancellationToken: cancellationToken).ConfigureAwait(false);
            await WaitForAsync(() => first.Received.Count + second.Received.Count >= 1, "point-to-point delivery").ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        // Assert: one delivery, then verify no second consumer received it.
        MSTestAssertions.That(() => (first.Received.Count + second.Received.Count).Should().Be(1));
    }

    /// <summary>A stopped transport rejects new messages instead of dropping them silently.</summary>
    [TestMethod]
    public async Task Stop_RejectsNewMessagesAsync() {
        // Arrange
        var serializer = CreateSerializer();
        var probe = new TransportProbe();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var transport = await CreateTransportAsync(serializer, probe).ConfigureAwait(false);
        await transport.StartAsync(timeout.Token).ConfigureAwait(false);

        try {
            // Act
            await transport.StopAsync(timeout.Token).ConfigureAwait(false);
            var thrown = await CaptureAsync(() => transport.SendAsync(new TransportContractMessage("late", 0), cancellationToken: timeout.Token)).ConfigureAwait(false);

            // Assert
            MSTestAssertions.That(() => thrown.Should().NotBeNull());
            MSTestAssertions.That(() => probe.Received.Should().BeEmpty());
        }
        finally {
            await DisposeTransportAsync(transport).ConfigureAwait(false);
        }
    }

    /// <summary>Lifecycle calls are idempotent.</summary>
    [TestMethod]
    public async Task StartAndStop_AreIdempotentAsync() {
        // Arrange
        var serializer = CreateSerializer();
        var probe = new TransportProbe();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var transport = await CreateTransportAsync(serializer, probe).ConfigureAwait(false);

        try {
            // Act
            await transport.StartAsync(timeout.Token).ConfigureAwait(false);
            await transport.StartAsync(timeout.Token).ConfigureAwait(false);
            await transport.SendAsync(new TransportContractMessage("once", 1), cancellationToken: timeout.Token).ConfigureAwait(false);
            await WaitForAsync(() => probe.Received.Count >= 1, "lifecycle delivery").ConfigureAwait(false);
            await transport.StopAsync(timeout.Token).ConfigureAwait(false);
            await transport.StopAsync(timeout.Token).ConfigureAwait(false);

            // Assert
            MSTestAssertions.That(() => probe.Received.Should().HaveCount(1));
        }
        finally {
            await DisposeTransportAsync(transport).ConfigureAwait(false);
        }
    }

    private static JsonMessageSerializer CreateSerializer() =>
        new(Options.Create(new JsonMessageSerializerOptions()));

    private async Task RunAsync(IMessageSerializer serializer, IMessageTransportHandler[] handlers, Func<IMessageTransport, CancellationToken, Task> body) {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var transport = await CreateTransportAsync(serializer, handlers).ConfigureAwait(false);
        await transport.StartAsync(timeout.Token).ConfigureAwait(false);

        try {
            await body(transport, timeout.Token).ConfigureAwait(false);
        }
        finally {
            try {
                await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally {
                await DisposeTransportAsync(transport).ConfigureAwait(false);
            }
        }
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

    private static async Task WaitForAsync(Func<bool> condition, string what) {
        var deadline = Environment.TickCount64 + 15_000;

        while(!condition()) {
            if(Environment.TickCount64 > deadline) {
                break;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        MSTestAssertions.That(() => condition().Should().BeTrue($"timed out waiting for: {what}"));
    }
}
