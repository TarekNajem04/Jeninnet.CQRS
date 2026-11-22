//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// Characterization tests for CQRS-47: the transport relay publishes staged rows once, applies
/// exponential backoff, dead-letters poison rows, preserves staged order, never lets two workers
/// own the same row, and reports lag/published/failed metrics.
/// </summary>
[TestClass]
[DoNotParallelize]
public class OutboxRelayTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task RelayOnce_PublishesStagedRowsInOrder_AndCompletesThemAsync() {
        // Arrange: the publisher only stages because the transport relay owns the outbox.
        await using var host = BuildHost();
        var publisher = host.Provider.GetRequiredService<INotificationPublisher>();
        await publisher.PublishAsync(new RelayNotification("a"), TestContext!.CancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new RelayNotification("b"), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert: nothing reached the transport yet.
        MSTestAssertions.That(() => host.Transport.Published.Should().BeEmpty());
        MSTestAssertions.That(() => host.Store.ListAsync(TestContext!.CancellationToken).GetAwaiter().GetResult().Should().HaveCount(2));

        // Act
        var result = await host.Worker.RelayOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => result.Leased.Should().Be(2));
        MSTestAssertions.That(() => result.Published.Should().Be(2));
        MSTestAssertions.That(() => result.Retried.Should().Be(0));
        MSTestAssertions.That(() => result.DeadLettered.Should().Be(0));
        MSTestAssertions.That(() => host.Transport.Published.Select(static message => ((RelayNotification)message.Message).Name).Should().Equal(["a", "b"]));
        MSTestAssertions.That(() => host.Transport.Published.All(static message => message.Options!.MessageId is not null).Should().BeTrue());
        MSTestAssertions.That(() => host.Store.ListAsync(TestContext!.CancellationToken).GetAwaiter().GetResult().Should().BeEmpty());
    }

    [TestMethod]
    public async Task FailedPublish_BacksOff_ThenSucceedsAsync() {
        // Arrange
        await using var host = BuildHost(options => {
            options.InitialRetryDelay = TimeSpan.FromSeconds(2);
            options.MaxAttempts = 5;
        });
        await StageAsync(host, "retry").ConfigureAwait(false);
        host.Transport.Fail = true;

        // Act: first pass fails and schedules the backoff.
        var first = await host.Worker.RelayOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => first.Leased.Should().Be(1));
        MSTestAssertions.That(() => first.Retried.Should().Be(1));
        MSTestAssertions.That(() => first.Published.Should().Be(0));
        MSTestAssertions.That(() => host.Store.ListAsync(TestContext!.CancellationToken).GetAwaiter().GetResult().Should().HaveCount(1));

        // Act: an immediate poll is blocked by the head-of-line backoff.
        var blocked = await host.Worker.RelayOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        MSTestAssertions.That(() => blocked.Leased.Should().Be(0));

        // Act: after the delay the same row is published.
        host.Clock.Advance(TimeSpan.FromSeconds(2));
        host.Transport.Fail = false;
        var second = await host.Worker.RelayOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => second.Published.Should().Be(1));
        MSTestAssertions.That(() => host.Transport.Published.Should().HaveCount(1));
        MSTestAssertions.That(() => host.Store.ListAsync(TestContext!.CancellationToken).GetAwaiter().GetResult().Should().BeEmpty());
    }

    [TestMethod]
    public async Task PoisonRow_MovesToDeadLettersAfterTheThresholdAsync() {
        // Arrange: two attempts, then poison.
        await using var host = BuildHost(options => {
            options.MaxAttempts = 2;
            options.InitialRetryDelay = TimeSpan.FromSeconds(1);
            options.MaxRetryDelay = TimeSpan.FromSeconds(1);
        });
        await StageAsync(host, "poison").ConfigureAwait(false);
        host.Transport.Fail = true;

        // Act: first failure retries.
        var first = await host.Worker.RelayOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        MSTestAssertions.That(() => first.Retried.Should().Be(1));

        host.Clock.Advance(TimeSpan.FromSeconds(1));
        var second = await host.Worker.RelayOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert: the row left the outbox and is inspectable with its error.
        MSTestAssertions.That(() => second.DeadLettered.Should().Be(1));
        MSTestAssertions.That(() => host.Store.ListAsync(TestContext!.CancellationToken).GetAwaiter().GetResult().Should().BeEmpty());
        var entries = await host.DeadLetters.ListAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        MSTestAssertions.That(() => entries.Should().HaveCount(1));
        MSTestAssertions.That(() => entries[0].LastErrorMessage.Should().Be("broker down"));
        MSTestAssertions.That(() => entries[0].CommandType.Should().Contain(nameof(RelayNotification)));
    }

    [TestMethod]
    public async Task TwoWorkers_OnOneStore_PublishExactlyOnceAsync() {
        // Arrange
        await using var host = BuildHost();
        await StageAsync(host, "once").ConfigureAwait(false);
        using var secondMetrics = new OutboxRelayMetrics(host.Store, host.Clock);
        var secondWorker = new OutboxRelayWorker<RecordingTransport>(
            host.Store,
            host.Transport,
            host.DeadLetters,
            Options.Create(new OutboxRelayOptions {
                OwnerId = "worker-b",
                BatchSize = 10,
                PollInterval = TimeSpan.FromMilliseconds(10),
                LeaseDuration = TimeSpan.FromSeconds(30),
                InitialRetryDelay = TimeSpan.FromSeconds(1),
                MaxRetryDelay = TimeSpan.FromSeconds(10),
                BackoffMultiplier = 2,
                MaxAttempts = 5,
            }),
            host.Clock,
            secondMetrics);

        // Act: both workers poll concurrently.
        var results = await Task.WhenAll(
            host.Worker.RelayOnceAsync(TestContext!.CancellationToken),
            secondWorker.RelayOnceAsync(TestContext!.CancellationToken)).ConfigureAwait(false);

        // Assert: the lease admits exactly one owner.
        MSTestAssertions.That(() => results.Sum(static result => result.Leased).Should().Be(1));
        MSTestAssertions.That(() => host.Transport.Published.Should().HaveCount(1));
        MSTestAssertions.That(() => host.Store.ListAsync(TestContext!.CancellationToken).GetAwaiter().GetResult().Should().BeEmpty());
    }

    [TestMethod]
    public async Task Failure_StopsTheBatchAndPreservesStagedOrderAsync() {
        // Arrange: three rows, a failing head, and a two-second backoff.
        await using var host = BuildHost(options => {
            options.BatchSize = 3;
            options.MaxAttempts = 5;
            options.StopOnFailure = true;
        });
        await StageAsync(host, "a").ConfigureAwait(false);
        await StageAsync(host, "b").ConfigureAwait(false);
        await StageAsync(host, "c").ConfigureAwait(false);
        host.Transport.Fail = true;

        // Act: the head fails, later rows are released, and the next poll is blocked by the peer.
        var first = await host.Worker.RelayOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        var blocked = await host.Worker.RelayOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => first.Leased.Should().Be(3));
        MSTestAssertions.That(() => first.Retried.Should().Be(1));
        MSTestAssertions.That(() => blocked.Leased.Should().Be(0));

        // Act: after the backoff the whole batch publishes in staged order.
        host.Clock.Advance(TimeSpan.FromSeconds(2));
        host.Transport.Fail = false;
        var second = await host.Worker.RelayOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => second.Published.Should().Be(3));
        MSTestAssertions.That(() => host.Transport.Published.Select(static message => ((RelayNotification)message.Message).Name).Should().Equal(["a", "b", "c"]));
    }

    [TestMethod]
    public async Task Metrics_ReportLagAndPublishedAndFailedAsync() {
        // Arrange: one ten-second-old row makes the lag observable.
        await using var host = BuildHost();
        await StageAsync(host, "old", ageSeconds: 10).ConfigureAwait(false);
        var longs = new Dictionary<string, long>(StringComparer.Ordinal);
        var doubles = new Dictionary<string, double>(StringComparer.Ordinal);
        var transports = new Dictionary<string, string?>(StringComparer.Ordinal);
        using var listener = new MeterListener {
            InstrumentPublished = static (instrument, meterListener) => {
                if(instrument.Meter.Name == OutboxRelayDiagnosticNames.MeterName &&
                   Array.IndexOf(_instrumentNames, instrument.Name) >= 0) {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => {
            longs[instrument.Name] = value;
            transports[instrument.Name] = Tag(tags, OutboxRelayDiagnosticNames.TransportTag);
        });
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => {
            doubles[instrument.Name] = value;
            transports[instrument.Name] = Tag(tags, OutboxRelayDiagnosticNames.TransportTag);
        });
        listener.Start();
        listener.RecordObservableInstruments();

        // Assert: lag is the age of the oldest pending row.
        MSTestAssertions.That(() => doubles[OutboxRelayDiagnosticNames.LagSeconds].Should().Be(10));

        // Act: a successful publish and a failed publish.
        await host.Worker.RelayOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        await StageAsync(host, "fail").ConfigureAwait(false);
        host.Transport.Fail = true;
        await host.Worker.RelayOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => longs[OutboxRelayDiagnosticNames.Published].Should().Be(1));
        MSTestAssertions.That(() => longs[OutboxRelayDiagnosticNames.Failed].Should().Be(1));
        MSTestAssertions.That(() => transports[OutboxRelayDiagnosticNames.Published].Should().Be(nameof(RecordingTransport)));
        MSTestAssertions.That(() => transports[OutboxRelayDiagnosticNames.Failed].Should().Be(nameof(RecordingTransport)));
    }

    [TestMethod]
    public async Task AddCqrsOutboxRelay_StandsTheInProcessDispatcherDownAsync() {
        // Arrange
        var clock = new ManualClock();
        var store = new InMemoryOutboxStore();
        var services = new ServiceCollection();
        services.AddSingleton<IOutboxStore>(store);
        services.AddSingleton<IMessageTransport>(new RecordingTransport());
        services.AddSingleton<IDeadLetterStore>(new InMemoryDeadLetterStore(Options.Create(new DeadLetterOptions())));
        services.AddSingleton<TimeProvider>(clock);
        services.AddCqrsOutboxRelay<RecordingTransport>();
        await using var provider = services.BuildServiceProvider();
        await store.StageAsync(new OutboxNotification<RelayNotification>(new RelayNotification("pending"), clock.GetUtcNow())).ConfigureAwait(false);
        var dispatcher = provider.GetServices<IHostedService>().OfType<OutboxDispatcher>().Single();

        // Act
        var delivered = await dispatcher.DispatchPendingAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        var options = provider.GetRequiredService<IOptions<OutboxRelayOptions>>().Value;

        // Assert: the relay owns the row; the in-process dispatcher did not consume it.
        MSTestAssertions.That(() => options.Enabled.Should().BeTrue());
        MSTestAssertions.That(() => delivered.Should().Be(0));
        MSTestAssertions.That(() => store.ListAsync(TestContext!.CancellationToken).GetAwaiter().GetResult().Should().HaveCount(1));
        MSTestAssertions.That(() => provider.GetServices<IHostedService>().OfType<OutboxRelayWorker<RecordingTransport>>().Should().HaveCount(1));
    }

    [TestMethod]
    public async Task RelayStore_IsObservedByTheOutboxHealthCheckAsync() {
        // Arrange: relay registration first, then durability health checks over the same store.
        var clock = new ManualClock();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IMessageTransport>(new RecordingTransport());
        services.AddSingleton<IDeadLetterStore>(new InMemoryDeadLetterStore(Options.Create(new DeadLetterOptions())));
        services.AddSingleton<TimeProvider>(clock);
        services.AddCqrsOutboxRelay<RecordingTransport>();
        services.AddCqrsDurabilityHealthChecks(options => {
            options.OutboxDegradedLag = TimeSpan.FromSeconds(5);
            options.OutboxUnhealthyLag = TimeSpan.FromSeconds(30);
        });
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IOutboxStore>();
        await store.StageAsync(new OutboxNotification<RelayNotification>(
            new RelayNotification("old"),
            clock.GetUtcNow().AddSeconds(-10))).ConfigureAwait(false);

        // Act
        var report = await provider.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => report.Entries["outbox"].Status.Should().Be(HealthStatus.Degraded));
        MSTestAssertions.That(() => report.Entries["outbox"].Data["lagSeconds"].Should().Be(10d));
    }

    private static readonly string[] _instrumentNames = [
        OutboxRelayDiagnosticNames.Published,
        OutboxRelayDiagnosticNames.Failed,
        OutboxRelayDiagnosticNames.LagSeconds,
    ];

    private static RelayHost BuildHost(Action<OutboxRelayOptions>? configure = null) {
        var clock = new ManualClock();
        var transport = new RecordingTransport();
        var deadLetters = new InMemoryDeadLetterStore(Options.Create(new DeadLetterOptions()));
        var services = new ServiceCollection();
        services.AddSingleton<IMessageTransport>(transport);
        services.AddSingleton<IDeadLetterStore>(deadLetters);
        services.AddSingleton<TimeProvider>(clock);
        services.AddCqrsOutboxRelay<RecordingTransport>(options => {
            options.OwnerId = "worker-a";
            options.BatchSize = 10;
            options.PollInterval = TimeSpan.FromMilliseconds(10);
            options.LeaseDuration = TimeSpan.FromSeconds(30);
            options.InitialRetryDelay = TimeSpan.FromSeconds(2);
            options.MaxRetryDelay = TimeSpan.FromSeconds(30);
            options.BackoffMultiplier = 2;
            options.MaxAttempts = 5;
            options.StopOnFailure = true;
            configure?.Invoke(options);
        });
        var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IOutboxStore>();
        var worker = provider.GetServices<IHostedService>().OfType<OutboxRelayWorker<RecordingTransport>>().Single();

        return new RelayHost(provider, store, transport, deadLetters, clock, worker);
    }

    private static Task<string> StageAsync(RelayHost host, string name, int ageSeconds = 0) =>
        host.Store.StageAsync(new OutboxNotification<RelayNotification>(
            new RelayNotification(name),
            host.Clock.GetUtcNow().AddSeconds(-ageSeconds)));

    private static string? Tag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key) {
        foreach(var tag in tags) {
            if(tag.Key == key) {
                return tag.Value as string;
            }
        }

        return null;
    }

    private sealed record RelayNotification(string Name) : INotification;

    private sealed record PublishedMessage(object Message, Type MessageType, TransportMessageOptions? Options);

    private sealed class RecordingTransport : IMessageTransport {
        private readonly Lock _sync = new();
        private readonly List<PublishedMessage> _published = [];

        internal bool Fail { get; set; }

        internal IReadOnlyList<PublishedMessage> Published {
            get {
                lock(_sync) {
                    return [.. _published];
                }
            }
        }

        public Task SendAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) =>
            PublishAsync(message, messageType, options, cancellationToken);

        public Task PublishAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) {
            if(Fail) {
                throw new InvalidOperationException("broker down");
            }

            lock(_sync) {
                _published.Add(new PublishedMessage(message, messageType, options));
            }

            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RelayHost(
        ServiceProvider provider,
        IOutboxStore store,
        RecordingTransport transport,
        InMemoryDeadLetterStore deadLetters,
        ManualClock clock,
        OutboxRelayWorker<RecordingTransport> worker) : IAsyncDisposable {
        internal ServiceProvider Provider { get; } = provider;

        internal IOutboxStore Store { get; } = store;

        internal RecordingTransport Transport { get; } = transport;

        internal InMemoryDeadLetterStore DeadLetters { get; } = deadLetters;

        internal ManualClock Clock { get; } = clock;

        internal OutboxRelayWorker<RecordingTransport> Worker { get; } = worker;

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private sealed class ManualClock : TimeProvider {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        internal void Advance(TimeSpan by) => _now += by;
    }
}
