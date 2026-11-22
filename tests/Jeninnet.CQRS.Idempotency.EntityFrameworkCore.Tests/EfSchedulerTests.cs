//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore.Tests;

/// <summary>A message used by the durable scheduler tests.</summary>
/// <param name="Value">The payload value.</param>
public sealed record ScheduledTestMessage(string Value);

/// <summary>
/// Characterization tests for the durable CQRS-51 scheduler: delayed and recurring schedules
/// survive a restart, and two stores over one database lease a due row exactly once.
/// </summary>
[TestClass]
public sealed class EfSchedulerTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task DelayedSchedule_SurvivesRestart_AndFiresOnceAsync() {
        // Arrange
        var file = NewDbFile();
        var clock = new ManualClock();
        var transport = new RecordingTransport();

        await using(var first = BuildHost(file, clock, transport)) {
            await first.Scheduler.ScheduleAsync(new ScheduledTestMessage("delayed"), TimeSpan.FromMinutes(1), cancellationToken: TestContext!.CancellationToken).ConfigureAwait(false);
        }

        // Act: a fresh host finds the pending row and fires it after the clock advances.
        clock.Advance(TimeSpan.FromMinutes(1));
        await using var second = BuildHost(file, clock, transport);
        var result = await second.Worker.RunOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => result.Fired.Should().Be(1));
        MSTestAssertions.That(() => transport.Published.Should().HaveCount(1));
        MSTestAssertions.That(() => transport.Published[0].Message.Should().Be(new ScheduledTestMessage("delayed")));
        MSTestAssertions.That(() => second.Store.ListAsync(TestContext!.CancellationToken).GetAwaiter().GetResult().Should().BeEmpty());
    }

    [TestMethod]
    public async Task RecurringSchedule_ReArmsAcrossRestartAsync() {
        // Arrange
        var file = NewDbFile();
        var clock = new ManualClock();
        var transport = new RecordingTransport();

        await using(var first = BuildHost(file, clock, transport)) {
            await first.Scheduler.ScheduleRecurringAsync(
                new ScheduledTestMessage("recurring"),
                CronExpression.Parse("* * * * *"),
                "minute",
                cancellationToken: TestContext!.CancellationToken).ConfigureAwait(false);
        }

        // Act: first occurrence, then restart and the next occurrence.
        clock.Advance(TimeSpan.FromMinutes(1));
        await using(var second = BuildHost(file, clock, transport)) {
            await second.Worker.RunOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        }

        clock.Advance(TimeSpan.FromMinutes(1));
        await using var third = BuildHost(file, clock, transport);
        await third.Worker.RunOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        var pending = await third.Store.ListAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => transport.Published.Should().HaveCount(2));
        MSTestAssertions.That(() => pending.Should().HaveCount(1));
        MSTestAssertions.That(() => pending[0].Key.Should().Be("minute"));
        MSTestAssertions.That(() => (pending[0].NextRunUtc > clock.GetUtcNow()).Should().BeTrue());
    }

    [TestMethod]
    public async Task TwoStores_LeaseOneDueRowOnlyAsync() {
        // Arrange
        var file = NewDbFile();
        var clock = new ManualClock();
        var transport = new RecordingTransport();

        await using var first = BuildHost(file, clock, transport);
        await first.Scheduler.ScheduleAsync(new ScheduledTestMessage("once"), TimeSpan.Zero, cancellationToken: TestContext!.CancellationToken).ConfigureAwait(false);
        await using var second = BuildHost(file, clock, transport);

        // Act: sequential claims over the same database; the second sees the first lease.
        var firstLease = await first.Store.LeaseDueAsync("worker-a", 10, clock.GetUtcNow(), TimeSpan.FromSeconds(30), TestContext!.CancellationToken).ConfigureAwait(false);
        var secondLease = await second.Store.LeaseDueAsync("worker-b", 10, clock.GetUtcNow(), TimeSpan.FromSeconds(30), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => firstLease.Should().HaveCount(1));
        MSTestAssertions.That(() => secondLease.Should().BeEmpty());
    }

    [TestMethod]
    public async Task EfScheduler_FailListCancelAndCountBranchesAsync() {
        // Arrange
        var file = NewDbFile();
        var clock = new ManualClock();
        var transport = new RecordingTransport();
        await using var host = BuildHost(file, clock, transport);
        var store = host.Store;
        var now = clock.GetUtcNow();
        var cancellationToken = TestContext!.CancellationToken;
        await host.Scheduler.ScheduleAsync(new ScheduledTestMessage("branches"), TimeSpan.Zero, cancellationToken: cancellationToken).ConfigureAwait(false);

        var listed = await store.ListAsync(cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(1, listed.Count);
        Assert.AreEqual(1L, await store.CountDueAsync(now, cancellationToken).ConfigureAwait(false));

        // Act: a stale owner cannot fail the row; the owner can.
        var leased = await store.LeaseDueAsync("worker-a", 10, now, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(1, leased.Count);
        var key = leased[0].Message.Key;
        var version = leased[0].Message.Version;

        Assert.IsFalse(await store.FailAsync(key, "other", version, new InvalidOperationException("boom"), 1, now.AddSeconds(5), cancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await store.FailAsync("missing", "worker-a", version, new InvalidOperationException("boom"), 1, now, cancellationToken).ConfigureAwait(false));
        Assert.IsTrue(await store.FailAsync(key, "worker-a", version, new InvalidOperationException("boom"), 1, now.AddSeconds(5), cancellationToken).ConfigureAwait(false));
        Assert.AreEqual(0L, await store.CountDueAsync(now, cancellationToken).ConfigureAwait(false));

        // Assert: cancel is tenant/key scoped and missing rows are a no-op.
        Assert.IsFalse(await store.CancelAsync("missing", cancellationToken).ConfigureAwait(false));
        Assert.IsTrue(await store.CancelAsync(key, cancellationToken).ConfigureAwait(false));
        Assert.AreEqual(0, (await store.ListAsync(cancellationToken).ConfigureAwait(false)).Count);
        Assert.AreEqual(0L, await store.CountDueAsync(now.AddDays(1), cancellationToken).ConfigureAwait(false));
    }

    private static string NewDbFile() =>
        Path.Combine(Path.GetTempPath(), $"cqrs51-scheduler-{Guid.NewGuid():N}.db");

    private static SchedulerHost BuildHost(string file, ManualClock clock, RecordingTransport transport) {
        var services = new ServiceCollection();
        services.AddSingleton<IMessageTransport>(transport);
        services.AddSingleton<TimeProvider>(clock);
        services.AddCqrsEfScheduler(
            db => db.UseSqlite($"Data Source={file}"),
            options => {
                options.OwnerId = "scheduler-worker";
                options.BatchSize = 10;
                options.PollInterval = TimeSpan.FromMilliseconds(10);
                options.LeaseDuration = TimeSpan.FromSeconds(30);
                options.InitialRetryDelay = TimeSpan.FromSeconds(1);
                options.MaxRetryDelay = TimeSpan.FromSeconds(10);
                options.BackoffMultiplier = 2;
            });
        var provider = services.BuildServiceProvider();

        using(var scope = provider.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>().CreateDbContext();
            db.Database.EnsureCreated();
        }

        return new SchedulerHost(
            provider,
            provider.GetRequiredService<IMessageScheduler>(),
            provider.GetRequiredService<ISchedulerStore>(),
            provider.GetRequiredService<SchedulerWorker>());
    }

    private sealed class SchedulerHost(
        ServiceProvider provider,
        IMessageScheduler scheduler,
        ISchedulerStore store,
        SchedulerWorker worker) : IAsyncDisposable {
        internal IMessageScheduler Scheduler { get; } = scheduler;

        internal ISchedulerStore Store { get; } = store;

        internal SchedulerWorker Worker { get; } = worker;

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }

    private sealed class ManualClock : TimeProvider {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        internal void Advance(TimeSpan by) => _now += by;
    }

    private sealed record PublishedMessage(object Message, Type MessageType, TransportMessageOptions? Options);

    private sealed class RecordingTransport : IMessageTransport {
        private readonly Lock _sync = new();
        private readonly List<PublishedMessage> _published = [];

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
            lock(_sync) {
                _published.Add(new PublishedMessage(message, messageType, options));
            }

            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}