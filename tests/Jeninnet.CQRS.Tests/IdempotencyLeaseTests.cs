//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// CQRS-40 characterization for the in-memory idempotency lease: the explicit
/// Free → Leased → Completed table, expiry/renewal, atomic acquisition, and the behavior's
/// wait, fail-fast, timeout, and release-on-failure paths.
/// </summary>
[TestClass]
public class IdempotencyLeaseTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task LeaseStateMachine_FreeLeasedCompleted_WithRenewalAsync() {
        // Arrange
        var clock = new ManualTimeProvider();
        await using var provider = BuildHost(clock);
        var store = provider.GetRequiredService<IIdempotencyStore>();
        var ttl = TimeSpan.FromSeconds(10);

        // Free → Leased
        var first = await store.TryAcquireAsync<string>("key", "owner-1", ttl, TestContext!.CancellationToken);
        var busy = await store.TryAcquireAsync<string>("key", "owner-2", ttl, TestContext!.CancellationToken);

        MSTestAssertions.That(() => first.State.Should().Be(IdempotencyLeaseState.Acquired));
        MSTestAssertions.That(() => busy.State.Should().Be(IdempotencyLeaseState.Busy));
        MSTestAssertions.That(() => busy.Owner.Should().Be("owner-1"));

        // Renewal extends the original expiry (t=8s, renewed to t=18s).
        clock.Advance(TimeSpan.FromSeconds(8));
        var renewed = await store.RenewAsync("key", "owner-1", ttl, TestContext!.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(5));
        var stillBusy = await store.TryAcquireAsync<string>("key", "owner-2", ttl, TestContext!.CancellationToken);

        MSTestAssertions.That(() => renewed.Should().BeTrue());
        MSTestAssertions.That(() => stillBusy.State.Should().Be(IdempotencyLeaseState.Busy));

        // A stale owner cannot complete; the real owner can.
        var stale = await store.CompleteAsync("key", "owner-2", "wrong", TestContext!.CancellationToken);
        var completed = await store.CompleteAsync("key", "owner-1", "answer", TestContext!.CancellationToken);
        var replay = await store.TryAcquireAsync<string>("key", "owner-3", ttl, TestContext!.CancellationToken);
        var stored = await store.GetAsync<string>("key", TestContext!.CancellationToken);

        MSTestAssertions.That(() => stale.Should().BeFalse());
        MSTestAssertions.That(() => completed.Should().BeTrue());
        MSTestAssertions.That(() => replay.State.Should().Be(IdempotencyLeaseState.Completed));
        MSTestAssertions.That(() => replay.Response.Should().Be("answer"));
        MSTestAssertions.That(() => stored.Should().Be("answer"));
    }

    [TestMethod]
    public async Task ExpiredLease_IsReclaimedByTheNextCallerAsync() {
        // Arrange: a crashed owner never completes.
        var clock = new ManualTimeProvider();
        await using var provider = BuildHost(clock);
        var store = provider.GetRequiredService<IIdempotencyStore>();
        var ttl = TimeSpan.FromSeconds(10);
        _ = await store.TryAcquireAsync<string>("key", "crashed", ttl, TestContext!.CancellationToken);

        // Act
        clock.Advance(TimeSpan.FromSeconds(11));
        var reclaimed = await store.TryAcquireAsync<string>("key", "next", ttl, TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => reclaimed.State.Should().Be(IdempotencyLeaseState.Acquired));
        MSTestAssertions.That(() => reclaimed.Owner.Should().Be("next"));
    }

    [TestMethod]
    public async Task ParallelAcquire_OnlyOneCallerWinsAsync() {
        // Arrange
        var clock = new ManualTimeProvider();
        await using var provider = BuildHost(clock);
        var store = provider.GetRequiredService<IIdempotencyStore>();

        // Act: 16 callers race for the same key.
        var attempts = Enumerable.Range(0, 16)
            .Select(index => Task.Run(async () => {
                await Task.Yield();
                return await store.TryAcquireAsync<string>("key", $"owner-{index}", TimeSpan.FromSeconds(10), TestContext!.CancellationToken);
            }))
            .ToList();
        var results = await Task.WhenAll(attempts);

        // Assert: exactly one acquired; everyone else observed the winner.
        MSTestAssertions.That(() => results.Count(static result => result.State == IdempotencyLeaseState.Acquired).Should().Be(1));
        MSTestAssertions.That(() => results.Count(static result => result.State == IdempotencyLeaseState.Busy).Should().Be(15));
    }

    [TestMethod]
    public async Task Behavior_EightConcurrentDispatches_RunTheHandlerOnceAsync() {
        // Arrange
        var clock = new ManualTimeProvider();
        await using var provider = BuildHost(clock, static options => {
            options.LeaseTimeToLive = TimeSpan.FromSeconds(5);
            options.PollInterval = TimeSpan.FromMilliseconds(10);
        });
        var behavior = CreateBehavior(provider);
        var executions = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<string> Next(LeasePing _, CancellationToken __) {
            Interlocked.Increment(ref executions);
            started.TrySetResult();
            await release.Task.ConfigureAwait(false);

            return "pong";
        }

        // Act: eight callers with the same key, one handler execution blocked until all wait.
        var calls = Enumerable.Range(0, 8)
            .Select(_ => behavior.HandleAsync(new LeasePing("same-key"), Next, TestContext!.CancellationToken))
            .ToList();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext!.CancellationToken);
        await Task.Delay(100, TestContext!.CancellationToken);
        release.SetResult();
        var responses = await Task.WhenAll(calls);

        // Assert
        MSTestAssertions.That(() => executions.Should().Be(1));
        MSTestAssertions.That(() => responses.Should().Equal(Enumerable.Repeat("pong", 8)));
    }

    [TestMethod]
    public async Task Behavior_FailFast_ThrowsWhileAnotherOwnerRunsAsync() {
        // Arrange
        var clock = new ManualTimeProvider();
        await using var provider = BuildHost(clock, static options => options.FailFastWhenBusy = true);
        var store = provider.GetRequiredService<IIdempotencyStore>();
        var behavior = CreateBehavior(provider);
        _ = await store.TryAcquireAsync<string>("busy-key", "owner-1", TimeSpan.FromMinutes(1), TestContext!.CancellationToken);
        var executions = 0;

        // Act
        var thrown = await CaptureAsync(() => behavior.HandleAsync(
            new LeasePing("busy-key"),
            (_, _) => {
                executions++;
                return Task.FromResult("unexpected");
            },
            TestContext!.CancellationToken));

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<ConcurrentExecutionException>());
        MSTestAssertions.That(() => ((ConcurrentExecutionException)thrown!).Owner.Should().Be("owner-1"));
        MSTestAssertions.That(() => thrown!.Message.Should().Contain("busy-key"));
        MSTestAssertions.That(() => thrown!.Message.Should().Contain("owner-1"));
        MSTestAssertions.That(() => executions.Should().Be(0));
    }

    [TestMethod]
    public async Task Behavior_WaitTimeout_ThrowsWhenTheOwnerNeverCompletesAsync() {
        // Arrange
        var clock = new ManualTimeProvider();
        await using var provider = BuildHost(clock, static options => {
            options.WaitTimeout = TimeSpan.FromMilliseconds(100);
            options.PollInterval = TimeSpan.FromMilliseconds(10);
        });
        var store = provider.GetRequiredService<IIdempotencyStore>();
        var behavior = CreateBehavior(provider);
        _ = await store.TryAcquireAsync<string>("stuck-key", "owner-1", TimeSpan.FromMinutes(1), TestContext!.CancellationToken);

        // Act
        var thrown = await CaptureAsync(() => behavior.HandleAsync(
            new LeasePing("stuck-key"),
            (_, _) => Task.FromResult("unexpected"),
            TestContext!.CancellationToken));

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<ConcurrentExecutionException>());
        MSTestAssertions.That(() => thrown!.Message.Should().Contain("Timed out"));
        MSTestAssertions.That(() => thrown!.Message.Should().Contain("stuck-key"));
    }

    [TestMethod]
    public void Behavior_InvalidOptions_FailAtConstruction() {
        // Arrange
        var store = new CapturingStore();
        var options = Options.Create(new IdempotencyBehaviorOptions { WaitTimeout = TimeSpan.Zero });

        // Act
        Exception? thrown = null;
        try {
            _ = new IdempotencyBehavior<LeasePing, string>(store, options);
        }
        catch(Exception exception) {
            thrown = exception;
        }

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<ArgumentOutOfRangeException>());
    }

    [TestMethod]
    public async Task Behavior_UsesTheTenantComposedKeyAsync() {
        // Arrange
        var store = new CapturingStore();
        var behavior = new IdempotencyBehavior<LeasePing, string>(
            store,
            Options.Create(new IdempotencyBehaviorOptions()),
            new FixedTenantKey());

        // Act
        var result = await behavior.HandleAsync(new LeasePing("tenant-key"), (_, _) => Task.FromResult("ok"), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => result.Should().Be("ok"));
        MSTestAssertions.That(() => store.LastKey.Should().Be("tenant:tenant-key"));
    }

    [TestMethod]
    public async Task Behavior_CompletionLeaseLoss_ThrowsWithTheProcessOwnerAsync() {
        // Arrange
        var behavior = new IdempotencyBehavior<LeasePing, string>(
            new LeaseLosingStore(),
            Options.Create(new IdempotencyBehaviorOptions()));

        // Act
        var thrown = await CaptureAsync(() => behavior.HandleAsync(new LeasePing("lost-key"), (_, _) => Task.FromResult("ok"), TestContext!.CancellationToken)).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<ConcurrentExecutionException>());
        MSTestAssertions.That(() => ((ConcurrentExecutionException)thrown!).Owner.Should().StartWith($"{Environment.MachineName}:{Environment.ProcessId}:"));
        MSTestAssertions.That(() => thrown!.Message.Should().Contain("lost-key"));
    }

    [TestMethod]
    public async Task Behavior_HeartbeatRenewalFailure_StopsRenewingAsync() {
        // Arrange
        var store = new ThrowingRenewStore();
        var behavior = new IdempotencyBehavior<LeasePing, string>(
            store,
            Options.Create(new IdempotencyBehaviorOptions {
                LeaseTimeToLive = TimeSpan.FromSeconds(5),
                HeartbeatInterval = TimeSpan.FromMilliseconds(10),
            }));

        // Act
        var result = await behavior.HandleAsync(
            new LeasePing("renew-throws"),
            async (_, cancellationToken) => {
                await Task.Delay(120, cancellationToken).ConfigureAwait(false);

                return "ok";
            },
            TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => result.Should().Be("ok"));
        MSTestAssertions.That(() => store.Renewals.Should().Be(1));
    }

    [TestMethod]
    public async Task Behavior_HeartbeatStopsWhenRenewalIsLostAsync() {
        // Arrange
        var store = new FailingRenewStore();
        var behavior = new IdempotencyBehavior<LeasePing, string>(
            store,
            Options.Create(new IdempotencyBehaviorOptions {
                LeaseTimeToLive = TimeSpan.FromSeconds(5),
                HeartbeatInterval = TimeSpan.FromMilliseconds(10),
            }));

        // Act
        var result = await behavior.HandleAsync(
            new LeasePing("renew-lost"),
            async (_, cancellationToken) => {
                await Task.Delay(120, cancellationToken).ConfigureAwait(false);

                return "ok";
            },
            TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => result.Should().Be("ok"));
        MSTestAssertions.That(() => store.Renewals.Should().Be(1));
    }

    [TestMethod]
    public async Task Behavior_HandlerFailure_ReleasesTheLeaseAsync() {
        // Arrange
        var clock = new ManualTimeProvider();
        await using var provider = BuildHost(clock);
        var store = provider.GetRequiredService<IIdempotencyStore>();
        var behavior = CreateBehavior(provider);

        // Act: the handler fails, then another owner must be able to acquire immediately.
        var thrown = await CaptureAsync(() => behavior.HandleAsync(
            new LeasePing("failing-key"),
            (_, _) => throw new InvalidOperationException("boom"),
            TestContext!.CancellationToken));
        var reacquired = await store.TryAcquireAsync<string>("failing-key", "next", TimeSpan.FromMinutes(1), TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<InvalidOperationException>());
        MSTestAssertions.That(() => reacquired.State.Should().Be(IdempotencyLeaseState.Acquired));
    }

    [TestMethod]
    public async Task Behavior_Heartbeat_RenewsALongRunningLeaseAsync() {
        // Arrange
        var clock = new ManualTimeProvider();
        await using var provider = BuildHost(clock, static options => {
            options.LeaseTimeToLive = TimeSpan.FromSeconds(5);
            options.HeartbeatInterval = TimeSpan.FromMilliseconds(20);
        });
        var store = new RenewalCountingStore(provider.GetRequiredService<IIdempotencyStore>());
        var behavior = new IdempotencyBehavior<LeasePing, string>(store, provider.GetRequiredService<IOptions<IdempotencyBehaviorOptions>>());
        var executions = 0;

        // Act: the handler outlives several heartbeat intervals.
        var result = await behavior.HandleAsync(
            new LeasePing("heartbeat-key"),
            async (_, cancellationToken) => {
                Interlocked.Increment(ref executions);

                // Wait for the heartbeat instead of racing a fixed delay under load.
                var renewalDeadline = Environment.TickCount64 + 5_000;

                while(store.Renewals is 0 && Environment.TickCount64 < renewalDeadline) {
                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                }

                return "done";
            },
            TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => result.Should().Be("done"));
        MSTestAssertions.That(() => executions.Should().Be(1));
        MSTestAssertions.That(() => store.Renewals.Should().BeGreaterThan(0));
    }

    private static ServiceProvider BuildHost(TimeProvider clock, Action<IdempotencyBehaviorOptions>? configure = null) {
        var services = new ServiceCollection();
        services.AddSingleton(clock);
        services.AddCqrsIdempotencyBehavior(configure);

        return services.BuildServiceProvider();
    }

    private static IdempotencyBehavior<LeasePing, string> CreateBehavior(IServiceProvider provider) =>
        new(provider.GetRequiredService<IIdempotencyStore>(), provider.GetRequiredService<IOptions<IdempotencyBehaviorOptions>>());

    private static async Task<Exception?> CaptureAsync(Func<Task> action) {
        try {
            await action().ConfigureAwait(false);

            return null;
        }
        catch(Exception exception) {
            return exception;
        }
    }

    private sealed record LeasePing(string Key) : IIdempotentCommand<string> {
        public string IdempotencyKey => Key;
    }

    private sealed class FixedTenantKey : ITenantScopedKey {
        public string Compose(string key) => $"tenant:{key}";
    }

    private sealed class CapturingStore : IIdempotencyStore {
        internal string? LastKey { get; private set; }

        public Task<TResponse?> GetAsync<TResponse>(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<TResponse?>(default);

        public Task StoreAsync<TResponse>(string key, TResponse response, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IdempotencyLeaseResult<TResponse>> TryAcquireAsync<TResponse>(string key, string owner, TimeSpan timeToLive, CancellationToken cancellationToken = default) {
            LastKey = key;

            return Task.FromResult(IdempotencyLeaseResult<TResponse>.Acquired(owner, DateTimeOffset.UtcNow + timeToLive));
        }

        public Task<bool> RenewAsync(string key, string owner, TimeSpan timeToLive, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> CompleteAsync<TResponse>(string key, string owner, TResponse response, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> ReleaseAsync(string key, string owner, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class LeaseLosingStore : IIdempotencyStore {
        public Task<TResponse?> GetAsync<TResponse>(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<TResponse?>(default);

        public Task StoreAsync<TResponse>(string key, TResponse response, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IdempotencyLeaseResult<TResponse>> TryAcquireAsync<TResponse>(string key, string owner, TimeSpan timeToLive, CancellationToken cancellationToken = default) =>
            Task.FromResult(IdempotencyLeaseResult<TResponse>.Acquired(owner, DateTimeOffset.UtcNow + timeToLive));

        public Task<bool> RenewAsync(string key, string owner, TimeSpan timeToLive, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> CompleteAsync<TResponse>(string key, string owner, TResponse response, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> ReleaseAsync(string key, string owner, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class ThrowingRenewStore : IIdempotencyStore {
        private int _renewals;

        internal int Renewals => Volatile.Read(ref _renewals);

        public Task<TResponse?> GetAsync<TResponse>(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<TResponse?>(default);

        public Task StoreAsync<TResponse>(string key, TResponse response, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IdempotencyLeaseResult<TResponse>> TryAcquireAsync<TResponse>(string key, string owner, TimeSpan timeToLive, CancellationToken cancellationToken = default) =>
            Task.FromResult(IdempotencyLeaseResult<TResponse>.Acquired(owner, DateTimeOffset.UtcNow + timeToLive));

        public Task<bool> RenewAsync(string key, string owner, TimeSpan timeToLive, CancellationToken cancellationToken = default) {
            Interlocked.Increment(ref _renewals);

            throw new InvalidOperationException("store down");
        }

        public Task<bool> CompleteAsync<TResponse>(string key, string owner, TResponse response, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> ReleaseAsync(string key, string owner, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class FailingRenewStore : IIdempotencyStore {
        private int _renewals;

        internal int Renewals => Volatile.Read(ref _renewals);

        public Task<TResponse?> GetAsync<TResponse>(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<TResponse?>(default);

        public Task StoreAsync<TResponse>(string key, TResponse response, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IdempotencyLeaseResult<TResponse>> TryAcquireAsync<TResponse>(string key, string owner, TimeSpan timeToLive, CancellationToken cancellationToken = default) =>
            Task.FromResult(IdempotencyLeaseResult<TResponse>.Acquired(owner, DateTimeOffset.UtcNow + timeToLive));

        public Task<bool> RenewAsync(string key, string owner, TimeSpan timeToLive, CancellationToken cancellationToken = default) {
            Interlocked.Increment(ref _renewals);

            return Task.FromResult(false);
        }

        public Task<bool> CompleteAsync<TResponse>(string key, string owner, TResponse response, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> ReleaseAsync(string key, string owner, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class RenewalCountingStore(IIdempotencyStore inner) : IIdempotencyStore {
        private int _renewals;

        public int Renewals => Volatile.Read(ref _renewals);

        public Task<TResponse?> GetAsync<TResponse>(string key, CancellationToken cancellationToken = default) =>
            inner.GetAsync<TResponse>(key, cancellationToken);

        public Task StoreAsync<TResponse>(string key, TResponse response, CancellationToken cancellationToken = default) =>
            inner.StoreAsync(key, response, cancellationToken);

        public Task<IdempotencyLeaseResult<TResponse>> TryAcquireAsync<TResponse>(string key, string owner, TimeSpan timeToLive, CancellationToken cancellationToken = default) =>
            inner.TryAcquireAsync<TResponse>(key, owner, timeToLive, cancellationToken);

        public Task<bool> RenewAsync(string key, string owner, TimeSpan timeToLive, CancellationToken cancellationToken = default) {
            Interlocked.Increment(ref _renewals);

            return inner.RenewAsync(key, owner, timeToLive, cancellationToken);
        }

        public Task<bool> CompleteAsync<TResponse>(string key, string owner, TResponse response, CancellationToken cancellationToken = default) =>
            inner.CompleteAsync(key, owner, response, cancellationToken);

        public Task<bool> ReleaseAsync(string key, string owner, CancellationToken cancellationToken = default) =>
            inner.ReleaseAsync(key, owner, cancellationToken);
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
