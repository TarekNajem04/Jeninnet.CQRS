//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//

namespace Jeninnet.CQRS.Transports.Tests;

/// <summary>
/// CQRS-57 consumer inbox through the in-memory transport: the same broker message id runs the
/// handler once, a failed handler releases the claim, tenants stay isolated, manual completion
/// works, and the duplicate counter is emitted.
/// </summary>
[TestClass]
public sealed class ConsumerInboxTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task SameMessageId_DeliveredTwice_RunsTheHandlerOnceAsync() {
        // Arrange
        var services = new ServiceCollection();
        services.AddCqrsInbox();
        services.AddSingleton<CountingInboxHandler>();
        services.AddSingleton<IMessageTransportHandler>(static provider => provider.GetRequiredService<CountingInboxHandler>());
        services.AddCqrsInMemoryTransport();

        using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IMessageTransport>();
        var handler = provider.GetRequiredService<CountingInboxHandler>();
        var store = provider.GetRequiredService<IInboxStore>();

        // Act
        await SendAsync(transport, new InboxPayload("one"), "message-1").ConfigureAwait(false);
        await SendAsync(transport, new InboxPayload("one"), "message-1").ConfigureAwait(false);
        var processed = await store.IsProcessedAsync("message-1", null, TestContext!.CancellationToken).ConfigureAwait(false);
        var entry = await store.GetAsync("message-1", null, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => handler.Count.Should().Be(1));
        MSTestAssertions.That(() => processed.Should().BeTrue());
        MSTestAssertions.That(() => entry.Should().NotBeNull());
        MSTestAssertions.That(() => entry!.ResultHash.Should().NotBeNullOrWhiteSpace());
    }

    [TestMethod]
    public async Task HandlerFailure_ReleasesTheClaim_SoRedeliveryRunsAgainAsync() {
        // Arrange
        var services = new ServiceCollection();
        services.AddCqrsInbox();
        services.AddSingleton<FlakyInboxHandler>();
        services.AddSingleton<IMessageTransportHandler>(static provider => provider.GetRequiredService<FlakyInboxHandler>());
        services.AddCqrsInMemoryTransport();

        using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IMessageTransport>();
        var handler = provider.GetRequiredService<FlakyInboxHandler>();
        var store = provider.GetRequiredService<IInboxStore>();

        // Act: the first attempt fails, so the pending claim is abandoned.
        var first = await CaptureAsync(() => SendAsync(transport, new InboxPayload("fail"), "message-1")).ConfigureAwait(false);
        var processedAfterFailure = await store.IsProcessedAsync("message-1", null, TestContext!.CancellationToken).ConfigureAwait(false);

        // Act: the broker redelivery runs the handler again.
        await SendAsync(transport, new InboxPayload("fail"), "message-1").ConfigureAwait(false);
        var processedAfterRetry = await store.IsProcessedAsync("message-1", null, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => first.Should().BeOfType<InvalidOperationException>());
        MSTestAssertions.That(() => handler.Attempts.Should().Be(2));
        MSTestAssertions.That(() => processedAfterFailure.Should().BeFalse());
        MSTestAssertions.That(() => processedAfterRetry.Should().BeTrue());
    }

    [TestMethod]
    public async Task SameMessageId_DifferentTenants_BothRunAsync() {
        // Arrange
        var services = new ServiceCollection();
        services.AddCqrsInbox();
        services.AddSingleton<CountingInboxHandler>();
        services.AddSingleton<IMessageTransportHandler>(static provider => provider.GetRequiredService<CountingInboxHandler>());
        services.AddCqrsInMemoryTransport();

        using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IMessageTransport>();
        var handler = provider.GetRequiredService<CountingInboxHandler>();

        // Act
        await SendAsync(transport, new InboxPayload("a"), "message-1", "tenant-a").ConfigureAwait(false);
        await SendAsync(transport, new InboxPayload("b"), "message-1", "tenant-b").ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => handler.Count.Should().Be(2));
    }

    [TestMethod]
    public async Task ManualCompletion_ClosesTheInboxRowInsideTheHandlerAsync() {
        // Arrange
        var services = new ServiceCollection();
        services.AddCqrsInbox(options => options.AutoComplete = false);
        services.AddSingleton<ManualInboxHandler>();
        services.AddSingleton<IMessageTransportHandler>(static provider => provider.GetRequiredService<ManualInboxHandler>());
        services.AddCqrsInMemoryTransport();

        using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IMessageTransport>();
        var handler = provider.GetRequiredService<ManualInboxHandler>();

        // Act
        await SendAsync(transport, new InboxPayload("manual"), "message-1").ConfigureAwait(false);
        await SendAsync(transport, new InboxPayload("manual"), "message-1").ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => handler.Count.Should().Be(1));
    }

    [TestMethod]
    public async Task DuplicateDelivery_EmitsTheDuplicateMetricAsync() {
        // Arrange
        var expectedType = typeof(MetricInboxPayload).AssemblyQualifiedName;
        long duplicates = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = static (instrument, current) => {
            if(instrument.Meter.Name == InboxDiagnosticNames.MeterName) {
                current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) => {
            if(instrument.Name != InboxDiagnosticNames.Duplicates) {
                return;
            }

            foreach(var tag in tags) {
                if(tag.Key == InboxDiagnosticNames.MessageTypeTag && Equals(tag.Value, expectedType)) {
                    Interlocked.Add(ref duplicates, measurement);
                }
            }
        });
        listener.Start();

        var services = new ServiceCollection();
        services.AddCqrsInbox();
        services.AddSingleton<CountingInboxHandler>();
        services.AddSingleton<IMessageTransportHandler>(static provider => provider.GetRequiredService<CountingInboxHandler>());
        services.AddCqrsInMemoryTransport();

        using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IMessageTransport>();

        // Act
        await SendAsync(transport, new MetricInboxPayload("metric"), "metric-1").ConfigureAwait(false);
        await SendAsync(transport, new MetricInboxPayload("metric"), "metric-1").ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => Volatile.Read(ref duplicates).Should().Be(1));
    }

    private async Task SendAsync<TMessage>(
        IMessageTransport transport,
        TMessage message,
        string messageId,
        string? tenantId = null) {
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            await transport.SendAsync(
                message,
                new TransportMessageOptions { MessageId = messageId, TenantId = tenantId },
                TestContext.CancellationToken).ConfigureAwait(false);
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
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

    public sealed class CountingInboxHandler : IMessageTransportHandler {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public Task HandleAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default) {
            Interlocked.Increment(ref _count);

            return Task.CompletedTask;
        }
    }

    public sealed class FlakyInboxHandler : IMessageTransportHandler {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        public Task HandleAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default) {
            var attempt = Interlocked.Increment(ref _attempts);

            return attempt is 1
                ? Task.FromException(new InvalidOperationException("transient"))
                : Task.CompletedTask;
        }
    }

    public sealed class ManualInboxHandler(IInboxStore store) : IMessageTransportHandler {
        private readonly IInboxStore _store = store ?? throw new ArgumentNullException(nameof(store));
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public async Task HandleAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default) {
            Interlocked.Increment(ref _count);
            await _store
                .CompleteAsync(envelope.MessageId, envelope.TenantId, null, DateTimeOffset.UtcNow, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

/// <summary>A payload used by the consumer-inbox delivery tests.</summary>
/// <param name="Name">A payload.</param>
public sealed record InboxPayload(string Name);

/// <summary>A unique payload type so the duplicate metric test can filter its own tag.</summary>
/// <param name="Name">A payload.</param>
public sealed record MetricInboxPayload(string Name);
