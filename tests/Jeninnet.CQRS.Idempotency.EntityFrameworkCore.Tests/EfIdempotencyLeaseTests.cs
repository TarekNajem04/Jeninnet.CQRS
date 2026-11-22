//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore.Tests;

/// <summary>
/// CQRS-40 characterization on the durable store: atomic acquisition under parallel SQLite
/// callers, cross-host waits over one file, owner-checked completion/renewal, and lease expiry
/// reclaiming a crashed owner.
/// </summary>
[TestClass]
public class EfIdempotencyLeaseTests {
    public TestContext? TestContext { get; set; }

    private static string NewDbFile() => Path.Combine(Path.GetTempPath(), $"cqrs40-{Guid.NewGuid():N}.db");

    private static ServiceProvider BuildHost(string file, TimeProvider clock, Action<IdempotencyBehaviorOptions>? configureBehavior = null) {
        var services = new ServiceCollection();
        services.AddSingleton(clock);
        services.AddDbContextFactory<IdempotencyDbContext>(builder => builder.UseSqlite($"Data Source={file};Default Timeout=30"));
        services.AddCqrsEfIdempotencyStore(configureBehavior: configureBehavior);

        return services.BuildServiceProvider();
    }

    private static async Task PrepareDatabaseAsync(IServiceProvider provider, CancellationToken cancellationToken) {
        var factory = provider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>();
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ParallelAcquire_OnlyOneCallerWinsAsync() {
        // Arrange
        var clock = new ManualTimeProvider();
        var file = NewDbFile();
        await using var provider = BuildHost(file, clock);
        await PrepareDatabaseAsync(provider, TestContext!.CancellationToken);
        var store = provider.GetRequiredService<IIdempotencyStore>();

        // Act: twelve callers race for the same key.
        var attempts = Enumerable.Range(0, 12)
            .Select(index => Task.Run(async () => {
                await Task.Yield();
                return await store.TryAcquireAsync<string>("key", $"owner-{index}", TimeSpan.FromSeconds(30), TestContext!.CancellationToken);
            }))
            .ToList();
        var results = await Task.WhenAll(attempts);

        // Assert
        MSTestAssertions.That(() => results.Count(static result => result.State == IdempotencyLeaseState.Acquired).Should().Be(1));
        MSTestAssertions.That(() => results.Count(static result => result.State == IdempotencyLeaseState.Busy).Should().Be(11));
    }

    [TestMethod]
    public async Task CrossHost_ConcurrentDispatches_RunTheHandlerOnceAsync() {
        // Arrange: two hosts, one SQLite file (the cross-process shape).
        var clock = new ManualTimeProvider();
        var file = NewDbFile();
        await using var first = BuildHost(file, clock);
        await PrepareDatabaseAsync(first, TestContext!.CancellationToken);
        await using var second = BuildHost(file, clock);
        var options = new IdempotencyBehaviorOptions {
            WaitTimeout = TimeSpan.FromSeconds(15),
            LeaseTimeToLive = TimeSpan.FromSeconds(30),
            PollInterval = TimeSpan.FromMilliseconds(25),
        };
        var behaviorA = new IdempotencyBehavior<LeaseOrder, string>(first.GetRequiredService<IIdempotencyStore>(), Options.Create(options));
        var behaviorB = new IdempotencyBehavior<LeaseOrder, string>(second.GetRequiredService<IIdempotencyStore>(), Options.Create(options));
        var behaviors = new[] { behaviorA, behaviorB };
        var executions = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<string> Next(LeaseOrder _, CancellationToken __) {
            Interlocked.Increment(ref executions);
            started.TrySetResult();
            await release.Task.ConfigureAwait(false);

            return "paid";
        }

        // Act
        var calls = Enumerable.Range(0, 8)
            .Select(index => behaviors[index % behaviors.Length].HandleAsync(new LeaseOrder("order-1"), Next, TestContext!.CancellationToken))
            .ToList();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext!.CancellationToken);
        await Task.Delay(200, TestContext!.CancellationToken);
        release.SetResult();
        var responses = await Task.WhenAll(calls);

        // Assert: one execution, every caller sees the same stored response.
        var stored = await second.GetRequiredService<IIdempotencyStore>().GetAsync<string>("order-1", TestContext!.CancellationToken);
        MSTestAssertions.That(() => executions.Should().Be(1));
        MSTestAssertions.That(() => responses.Should().Equal(Enumerable.Repeat("paid", 8)));
        MSTestAssertions.That(() => stored.Should().Be("paid"));
    }

    [TestMethod]
    public async Task ExpiredLease_IsReclaimedByAnotherHostAsync() {
        // Arrange
        var clock = new ManualTimeProvider();
        var file = NewDbFile();
        await using var first = BuildHost(file, clock);
        await PrepareDatabaseAsync(first, TestContext!.CancellationToken);
        await using var second = BuildHost(file, clock);
        var ttl = TimeSpan.FromSeconds(10);
        _ = await first.GetRequiredService<IIdempotencyStore>().TryAcquireAsync<string>("crashed-key", "crashed", ttl, TestContext!.CancellationToken);

        // Act
        clock.Advance(TimeSpan.FromSeconds(11));
        var reclaimed = await second.GetRequiredService<IIdempotencyStore>().TryAcquireAsync<string>("crashed-key", "next", ttl, TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => reclaimed.State.Should().Be(IdempotencyLeaseState.Acquired));
        MSTestAssertions.That(() => reclaimed.Owner.Should().Be("next"));
    }

    [TestMethod]
    public async Task StaleOwner_CannotRenewOrCompleteAsync() {
        // Arrange
        var clock = new ManualTimeProvider();
        var file = NewDbFile();
        await using var first = BuildHost(file, clock);
        await PrepareDatabaseAsync(first, TestContext!.CancellationToken);
        await using var second = BuildHost(file, clock);
        var ownerStore = first.GetRequiredService<IIdempotencyStore>();
        var staleStore = second.GetRequiredService<IIdempotencyStore>();
        var ttl = TimeSpan.FromMinutes(1);
        _ = await ownerStore.TryAcquireAsync<string>("owned-key", "owner-1", ttl, TestContext!.CancellationToken);

        // Act
        var staleRenew = await staleStore.RenewAsync("owned-key", "owner-2", ttl, TestContext!.CancellationToken);
        var staleComplete = await staleStore.CompleteAsync("owned-key", "owner-2", "wrong", TestContext!.CancellationToken);
        var leasedRead = await staleStore.GetAsync<string>("owned-key", TestContext!.CancellationToken);
        var renewed = await ownerStore.RenewAsync("owned-key", "owner-1", ttl, TestContext!.CancellationToken);
        var completed = await ownerStore.CompleteAsync("owned-key", "owner-1", "answer", TestContext!.CancellationToken);
        var replay = await staleStore.TryAcquireAsync<string>("owned-key", "owner-3", ttl, TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => staleRenew.Should().BeFalse());
        MSTestAssertions.That(() => staleComplete.Should().BeFalse());
        MSTestAssertions.That(() => leasedRead.Should().BeNull());
        MSTestAssertions.That(() => renewed.Should().BeTrue());
        MSTestAssertions.That(() => completed.Should().BeTrue());
        MSTestAssertions.That(() => replay.State.Should().Be(IdempotencyLeaseState.Completed));
        MSTestAssertions.That(() => replay.Response.Should().Be("answer"));
    }

    [TestMethod]
    public async Task Behavior_HandlerFailure_ReleasesTheDurableLeaseAsync() {
        // Arrange
        var clock = new ManualTimeProvider();
        var file = NewDbFile();
        await using var provider = BuildHost(file, clock, static options => options.PollInterval = TimeSpan.FromMilliseconds(10));
        await PrepareDatabaseAsync(provider, TestContext!.CancellationToken);
        var store = provider.GetRequiredService<IIdempotencyStore>();
        var behavior = new IdempotencyBehavior<LeaseOrder, string>(store, provider.GetRequiredService<IOptions<IdempotencyBehaviorOptions>>());

        // Act
        var thrown = await CaptureAsync(() => behavior.HandleAsync(
            new LeaseOrder("failing-key"),
            (_, _) => throw new InvalidOperationException("boom"),
            TestContext!.CancellationToken));
        var reacquired = await store.TryAcquireAsync<string>("failing-key", "next", TimeSpan.FromMinutes(1), TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<InvalidOperationException>());
        MSTestAssertions.That(() => reacquired.State.Should().Be(IdempotencyLeaseState.Acquired));
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

    private sealed record LeaseOrder(string Key) : IIdempotentCommand<string> {
        public string IdempotencyKey => Key;
    }

    private sealed class ManualTimeProvider : TimeProvider {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void Advance(TimeSpan span) => _now += span;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            throw new NotSupportedException("The idempotency store only reads the current time.");
    }
}
