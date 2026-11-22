//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// Retention sweeper control flow: disabled and policy-less sweeps, a busy lease, per-store
/// isolation, cancellation between stores, and the hosted loop's failure handling.
/// </summary>
[TestClass]
public sealed class RetentionSweeperTests {
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task SweepOnce_DisabledOrPolicyLess_ReturnsEmptyAsync() {
        // Arrange
        using var metrics = new RetentionMetrics();
        var lease = new FakeLease(true);
        var disabled = new RetentionSweeper(
            [new FakeStore("a")],
            CreateOptions(options => options.Enabled = false),
            lease,
            metrics,
            TimeProvider.System,
            NullLogger<RetentionSweeper>.Instance);
        var policyLess = new RetentionSweeper(
            [new FakeStore("a")],
            Microsoft.Extensions.Options.Options.Create(new RetentionOptions()),
            lease,
            metrics,
            TimeProvider.System,
            NullLogger<RetentionSweeper>.Instance);

        // Act
        var disabledResults = await disabled.SweepOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        var policyLessResults = await policyLess.SweepOnceAsync(TestContext.CancellationToken).ConfigureAwait(false);

        // Assert
        Assert.AreEqual(0, disabledResults.Count);
        Assert.AreEqual(0, policyLessResults.Count);
        Assert.AreEqual(0, lease.AcquireCalls);
    }

    [TestMethod]
    public async Task SweepOnce_BusyLease_SkipsEveryStoreAsync() {
        // Arrange
        using var metrics = new RetentionMetrics();
        var store = new FakeStore("a");
        var sweeper = new RetentionSweeper(
            [store],
            CreateOptions(),
            new FakeLease(false),
            metrics,
            TimeProvider.System,
            NullLogger<RetentionSweeper>.Instance);

        // Act
        var results = await sweeper.SweepOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        Assert.AreEqual(0, results.Count);
        Assert.AreEqual(0, store.Calls);
        Assert.IsNull(sweeper.Snapshot().LastAttemptUtc);
    }

    [TestMethod]
    public async Task SweepOnce_FailingStore_DoesNotBlockTheOtherStoresAsync() {
        // Arrange
        using var metrics = new RetentionMetrics();
        var lease = new FakeLease(true);
        var failing = new FakeStore("a", fail: true);
        var healthy = new FakeStore("b");
        var sweeper = new RetentionSweeper(
            [failing, healthy],
            CreateOptions(options => options.AddPolicy("b", new RetentionPolicy { Retention = TimeSpan.FromDays(1) })),
            lease,
            metrics,
            TimeProvider.System,
            NullLogger<RetentionSweeper>.Instance);

        // Act
        var results = await sweeper.SweepOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        var snapshot = sweeper.Snapshot();

        // Assert: the healthy store still ran, the failure is visible, and the lease was released.
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("b", results[0].Store);
        Assert.AreEqual(1, failing.Calls);
        Assert.AreEqual(1, healthy.Calls);
        Assert.IsNotNull(snapshot.LastAttemptUtc);
        Assert.IsNull(snapshot.LastSuccessUtc);
        Assert.AreEqual("a failed", snapshot.LastError);
        Assert.IsTrue(lease.Released);
    }

    [TestMethod]
    public async Task SweepOnce_CancellationBetweenStores_ThrowsAndReleasesLeaseAsync() {
        // Arrange
        using var metrics = new RetentionMetrics();
        var lease = new FakeLease(true);
        using var cancellation = new CancellationTokenSource();
        var first = new FakeStore("a", onSweep: cancellation.Cancel);
        var sweeper = new RetentionSweeper(
            [first, new FakeStore("b")],
            CreateOptions(options => options.AddPolicy("b", new RetentionPolicy { Retention = TimeSpan.FromDays(1) })),
            lease,
            metrics,
            TimeProvider.System,
            NullLogger<RetentionSweeper>.Instance);

        // Act + Assert
        _ = await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => sweeper.SweepOnceAsync(cancellation.Token)).ConfigureAwait(false);
        Assert.IsTrue(lease.Released);
    }

    [TestMethod]
    public async Task HostedLoop_RecordsOptionFailures_AndStopsAsync() {
        // Arrange: an invalid batch size makes every pass throw before any store runs.
        using var metrics = new RetentionMetrics();
        var sweeper = new RetentionSweeper(
            [new FakeStore("a")],
            CreateOptions(options => {
                options.Interval = TimeSpan.FromMilliseconds(10);
                options.BatchSize = 0;
            }),
            new FakeLease(true),
            metrics,
            TimeProvider.System,
            NullLogger<RetentionSweeper>.Instance);

        // Act
        await sweeper.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            await WaitUntilAsync(() => sweeper.Snapshot().LastError is not null).ConfigureAwait(false);
        }
        finally {
            await sweeper.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        // Assert
        Assert.IsNotNull(sweeper.Snapshot().LastError);
    }

    [TestMethod]
    public async Task HostedLoop_Disabled_DoesNotSweepAsync() {
        // Arrange
        using var metrics = new RetentionMetrics();
        var store = new FakeStore("a");
        var sweeper = new RetentionSweeper(
            [store],
            CreateOptions(options => options.Enabled = false),
            new FakeLease(true),
            metrics,
            TimeProvider.System,
            NullLogger<RetentionSweeper>.Instance);

        // Act
        await sweeper.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        await Task.Delay(50, TestContext.CancellationToken).ConfigureAwait(false);
        await sweeper.StopAsync(CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.AreEqual(0, store.Calls);
    }

    private static IOptions<RetentionOptions> CreateOptions(Action<RetentionOptions>? configure = null) {
        var options = new RetentionOptions();
        options.AddPolicy("a", new RetentionPolicy { Retention = TimeSpan.FromDays(1) });
        configure?.Invoke(options);

        return Microsoft.Extensions.Options.Options.Create(options);
    }

    private static async Task WaitUntilAsync(Func<bool> condition) {
        var deadline = Environment.TickCount64 + (long)_timeout.TotalMilliseconds;

        while(!condition()) {
            if(Environment.TickCount64 > deadline) {
                throw new TimeoutException("The retention loop did not report a failure in time.");
            }

            await Task.Delay(20).ConfigureAwait(false);
        }
    }

    private sealed class FakeLease(bool available) : IRetentionLease {
        public int AcquireCalls { get; private set; }

        public bool Released { get; private set; }

        public Task<bool> TryAcquireAsync(string owner, TimeSpan duration, DateTimeOffset now, CancellationToken cancellationToken = default) {
            AcquireCalls++;

            return Task.FromResult(available);
        }

        public Task ReleaseAsync(string owner, CancellationToken cancellationToken = default) {
            Released = true;

            return Task.CompletedTask;
        }
    }

    private sealed class FakeStore(string name, bool fail = false, Action? onSweep = null) : IRetentionStore {
        public string Name => name;

        public int Calls { get; private set; }

        public Task<RetentionBatchResult> SweepAsync(
            RetentionPolicy policy,
            DateTimeOffset now,
            int batchSize,
            bool dryRun,
            CancellationToken cancellationToken = default) {
            Calls++;
            onSweep?.Invoke();

            if(fail) {
                throw new InvalidOperationException($"{name} failed");
            }

            return Task.FromResult(new RetentionBatchResult(name, 1, 1, 0, dryRun));
        }
    }
}
