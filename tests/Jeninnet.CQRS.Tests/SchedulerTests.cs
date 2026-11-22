//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>A message used by the scheduler characterization tests.</summary>
/// <param name="Value">The payload value.</param>
public sealed record ScheduledTestMessage(string Value);

/// <summary>
/// Characterization tests for CQRS-51: delayed, recurring, cancellation, lease exclusion, and
/// retry behavior on the persistent scheduler.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class SchedulerTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task DelayedMessage_FiresOnceAfterTheFakeClockAdvancesAsync() {
        // Arrange
        await using var host = BuildHost();
        var scheduler = host.Provider.GetRequiredService<IMessageScheduler>();
        var key = await scheduler.ScheduleAsync(new ScheduledTestMessage("delayed"), TimeSpan.FromMinutes(1), cancellationToken: TestContext!.CancellationToken).ConfigureAwait(false);

        // Act: before the due time nothing fires.
        var early = await host.Worker.RunOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var due = await host.Worker.RunOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => early.Fired.Should().Be(0));
        MSTestAssertions.That(() => due.Fired.Should().Be(1));
        MSTestAssertions.That(() => host.Transport.Published.Should().HaveCount(1));
        MSTestAssertions.That(() => host.Transport.Published[0].Options!.Headers![SchedulerHeaders.Key].Should().Be(key));
        MSTestAssertions.That(() => host.Store.ListAsync(TestContext!.CancellationToken).GetAwaiter().GetResult().Should().BeEmpty());
    }

    [TestMethod]
    public async Task RecurringCron_RefiresAtEachOccurrenceAsync() {
        // Arrange
        await using var host = BuildHost();
        var scheduler = host.Provider.GetRequiredService<IMessageScheduler>();
        await scheduler.ScheduleRecurringAsync(
            new ScheduledTestMessage("cron"),
            CronExpression.Parse("* * * * *"),
            "minute",
            cancellationToken: TestContext!.CancellationToken).ConfigureAwait(false);

        // Act
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var first = await host.Worker.RunOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        var afterFirst = await host.Store.ListAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await host.Worker.RunOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        var afterSecond = await host.Store.ListAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => first.Fired.Should().Be(1));
        MSTestAssertions.That(() => second.Fired.Should().Be(1));
        MSTestAssertions.That(() => host.Transport.Published.Should().HaveCount(2));
        MSTestAssertions.That(() => afterFirst.Should().HaveCount(1));
        MSTestAssertions.That(() => afterFirst[0].Key.Should().Be("minute"));
        MSTestAssertions.That(() => afterSecond.Should().HaveCount(1));
        MSTestAssertions.That(() => (afterSecond[0].NextRunUtc > host.Clock.GetUtcNow()).Should().BeTrue());
    }

    [TestMethod]
    public async Task TwoWorkers_LeaseOneDueRowOnlyAsync() {
        // Arrange: one worker from DI and a second with a distinct owner id.
        await using var host = BuildHost();
        var scheduler = host.Provider.GetRequiredService<IMessageScheduler>();
        await scheduler.ScheduleAsync(new ScheduledTestMessage("once"), TimeSpan.Zero, cancellationToken: TestContext!.CancellationToken).ConfigureAwait(false);
        using var secondMetrics = new SchedulerMetrics(host.Store, host.Clock);
        var secondWorker = new SchedulerWorker(
            host.Store,
            host.Transport,
            host.Provider.GetRequiredService<IMessageSerializerRegistry>(),
            Options.Create(new SchedulerOptions {
                OwnerId = "worker-b",
                BatchSize = 10,
                PollInterval = TimeSpan.FromMilliseconds(10),
                LeaseDuration = TimeSpan.FromSeconds(30),
                InitialRetryDelay = TimeSpan.FromSeconds(1),
                MaxRetryDelay = TimeSpan.FromSeconds(10),
                BackoffMultiplier = 2,
            }),
            host.Clock,
            secondMetrics);

        // Act
        var results = await Task.WhenAll(
            host.Worker.RunOnceAsync(TestContext!.CancellationToken),
            secondWorker.RunOnceAsync(TestContext!.CancellationToken)).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => results.Sum(static result => result.Fired).Should().Be(1));
        MSTestAssertions.That(() => host.Transport.Published.Should().HaveCount(1));
        MSTestAssertions.That(() => host.Store.ListAsync(TestContext!.CancellationToken).GetAwaiter().GetResult().Should().BeEmpty());
    }

    [TestMethod]
    public async Task Cancel_RemovesAFutureSchedule_ButDoesNotAbortAnAlreadyLeasedExecutionAsync() {
        // Arrange
        await using var host = BuildHost();
        var scheduler = host.Provider.GetRequiredService<IMessageScheduler>();
        var futureKey = await scheduler.ScheduleAsync(new ScheduledTestMessage("future"), TimeSpan.FromMinutes(5), cancellationToken: TestContext!.CancellationToken).ConfigureAwait(false);
        var dueKey = await scheduler.ScheduleAsync(new ScheduledTestMessage("due"), TimeSpan.Zero, cancellationToken: TestContext!.CancellationToken).ConfigureAwait(false);

        // Act: future cancellation prevents the fire.
        var cancelled = await scheduler.CancelAsync(futureKey, TestContext!.CancellationToken).ConfigureAwait(false);
        host.Clock.Advance(TimeSpan.FromMinutes(5));
        var dueLeases = await host.Store.LeaseDueAsync(host.Worker.Owner, 10, host.Clock.GetUtcNow(), TimeSpan.FromSeconds(30), TestContext!.CancellationToken).ConfigureAwait(false);
        var inFlightKey = dueLeases.Single(lease => lease.Message.Key == dueKey).Message.Key;
        await scheduler.CancelAsync(inFlightKey, TestContext!.CancellationToken).ConfigureAwait(false);
        await host.Worker.ProcessLeaseAsync(dueLeases.Single(lease => lease.Message.Key == inFlightKey), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert: the future schedule is gone; the already-leased execution still fired once.
        MSTestAssertions.That(() => cancelled.Should().BeTrue());
        MSTestAssertions.That(() => host.Transport.Published.Should().HaveCount(1));
        MSTestAssertions.That(() => host.Transport.Published[0].Message.Should().Be(new ScheduledTestMessage("due")));
        MSTestAssertions.That(() => host.Store.ListAsync(TestContext!.CancellationToken).GetAwaiter().GetResult().Should().BeEmpty());
    }

    [TestMethod]
    public async Task FailedPublish_BacksOff_ThenFiresAsync() {
        // Arrange
        await using var host = BuildHost();
        var scheduler = host.Provider.GetRequiredService<IMessageScheduler>();
        await scheduler.ScheduleAsync(new ScheduledTestMessage("retry"), TimeSpan.Zero, cancellationToken: TestContext!.CancellationToken).ConfigureAwait(false);
        host.Transport.Fail = true;

        // Act
        var first = await host.Worker.RunOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        var pending = await host.Store.ListAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        host.Clock.Advance(TimeSpan.FromSeconds(2));
        host.Transport.Fail = false;
        var second = await host.Worker.RunOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => first.Retried.Should().Be(1));
        MSTestAssertions.That(() => pending.Should().HaveCount(1));
        MSTestAssertions.That(() => pending[0].Attempts.Should().Be(1));
        MSTestAssertions.That(() => second.Fired.Should().Be(1));
        MSTestAssertions.That(() => host.Store.ListAsync(TestContext!.CancellationToken).GetAwaiter().GetResult().Should().BeEmpty());
    }

    [TestMethod]
    public async Task DueBacklog_IsVisibleToMetricsAndHealthAsync() {
        // Arrange
        var clock = new SchedulerTestClock();
        var transport = new RecordingTransport();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IMessageTransport>(transport);
        services.AddSingleton<TimeProvider>(clock);
        services.AddCqrsScheduler(options => {
            options.OwnerId = "metrics-worker";
            options.BatchSize = 10;
            options.PollInterval = TimeSpan.FromMilliseconds(10);
            options.LeaseDuration = TimeSpan.FromSeconds(30);
            options.InitialRetryDelay = TimeSpan.FromSeconds(1);
            options.MaxRetryDelay = TimeSpan.FromSeconds(10);
            options.BackoffMultiplier = 2;
        });
        services.AddCqrsSchedulerHealthChecks(options => {
            options.DueDegraded = 1;
            options.DueUnhealthy = 2;
        });
        await using var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<IMessageScheduler>();
        var worker = provider.GetRequiredService<SchedulerWorker>();
        var health = provider.GetRequiredService<HealthCheckService>();
        var store = provider.GetRequiredService<ISchedulerStore>();
        await scheduler.ScheduleAsync(new ScheduledTestMessage("backlog"), TimeSpan.Zero, cancellationToken: TestContext!.CancellationToken).ConfigureAwait(false);

        // Act: the scheduler metrics are created by the worker/metrics hosted service resolution.
        var dueBefore = await store.CountDueAsync(clock.GetUtcNow(), TestContext!.CancellationToken).ConfigureAwait(false);
        var report = await health.CheckHealthAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        await worker.RunOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        var dueAfter = await store.CountDueAsync(clock.GetUtcNow(), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => dueBefore.Should().Be(1));
        MSTestAssertions.That(() => report.Entries["scheduler"].Status.Should().Be(HealthStatus.Degraded));
        MSTestAssertions.That(() => dueAfter.Should().Be(0));
    }

    private static SchedulerHost BuildHost() {
        var clock = new SchedulerTestClock();
        var transport = new RecordingTransport();
        var services = new ServiceCollection();
        services.AddSingleton<IMessageTransport>(transport);
        services.AddSingleton<TimeProvider>(clock);
        services.AddCqrsScheduler(options => {
            options.OwnerId = "worker-a";
            options.BatchSize = 10;
            options.PollInterval = TimeSpan.FromMilliseconds(10);
            options.LeaseDuration = TimeSpan.FromSeconds(30);
            options.InitialRetryDelay = TimeSpan.FromSeconds(1);
            options.MaxRetryDelay = TimeSpan.FromSeconds(10);
            options.BackoffMultiplier = 2;
        });
        var provider = services.BuildServiceProvider();

        return new SchedulerHost(
            provider,
            clock,
            transport,
            provider.GetRequiredService<ISchedulerStore>(),
            provider.GetRequiredService<SchedulerWorker>());
    }

    private sealed class SchedulerHost(
        ServiceProvider provider,
        SchedulerTestClock clock,
        RecordingTransport transport,
        ISchedulerStore store,
        SchedulerWorker worker) : IAsyncDisposable {
        internal ServiceProvider Provider { get; } = provider;

        internal SchedulerTestClock Clock { get; } = clock;

        internal RecordingTransport Transport { get; } = transport;

        internal ISchedulerStore Store { get; } = store;

        internal SchedulerWorker Worker { get; } = worker;

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private sealed class SchedulerTestClock : TimeProvider {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        internal void Advance(TimeSpan by) => _now += by;
    }

    private sealed record PublishedScheduledMessage(object Message, Type MessageType, TransportMessageOptions? Options);

    private sealed class RecordingTransport : IMessageTransport {
        private readonly Lock _sync = new();
        private readonly List<PublishedScheduledMessage> _published = [];

        internal bool Fail { get; set; }

        internal IReadOnlyList<PublishedScheduledMessage> Published {
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
                _published.Add(new PublishedScheduledMessage(message, messageType, options));
            }

            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}