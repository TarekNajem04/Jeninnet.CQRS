//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports.Tests;

/// <summary>
/// CQRS-57 in-memory inbox: the first claim wins, completion blocks duplicates, abandonment
/// enables an immediate retry, expired pending rows are reclaimable, and pruning honors both the
/// retention and pending-timeout windows.
/// </summary>
[TestClass]
public sealed class InMemoryInboxStoreTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task BeginComplete_BlocksDuplicatesAndRecordsTheHashAsync() {
        // Arrange
        var now = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualClock(now);
        var store = new InMemoryInboxStore(clock);
        var message = new InboxMessage("m-1", now, now.AddMinutes(5));

        // Act
        var first = await store.TryBeginAsync(message, TestContext!.CancellationToken).ConfigureAwait(false);
        var duplicate = await store.TryBeginAsync(message, TestContext!.CancellationToken).ConfigureAwait(false);
        await store.CompleteAsync("m-1", null, "hash-1", now.AddSeconds(1), TestContext!.CancellationToken).ConfigureAwait(false);
        var after = await store.TryBeginAsync(message, TestContext!.CancellationToken).ConfigureAwait(false);
        var entry = await store.GetAsync("m-1", null, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => first.Should().BeTrue());
        MSTestAssertions.That(() => duplicate.Should().BeFalse());
        MSTestAssertions.That(() => after.Should().BeFalse());
        MSTestAssertions.That(() => entry.Should().NotBeNull());
        MSTestAssertions.That(() => entry!.IsProcessed.Should().BeTrue());
        MSTestAssertions.That(() => entry!.ResultHash.Should().Be("hash-1"));
    }

    [TestMethod]
    public async Task Abandon_AllowsAnImmediateRetryAsync() {
        // Arrange
        var now = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var store = new InMemoryInboxStore(new ManualClock(now));
        var message = new InboxMessage("m-1", now, now.AddMinutes(5), "tenant-a");

        // Act
        var first = await store.TryBeginAsync(message, TestContext!.CancellationToken).ConfigureAwait(false);
        await store.AbandonAsync("m-1", "tenant-a", TestContext!.CancellationToken).ConfigureAwait(false);
        var retry = await store.TryBeginAsync(message, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => first.Should().BeTrue());
        MSTestAssertions.That(() => retry.Should().BeTrue());
    }

    [TestMethod]
    public async Task ExpiredPendingRow_IsReclaimableAsync() {
        // Arrange
        var now = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualClock(now);
        var store = new InMemoryInboxStore(clock);
        var message = new InboxMessage("m-1", now, now.AddSeconds(5));

        // Act
        var first = await store.TryBeginAsync(message, TestContext!.CancellationToken).ConfigureAwait(false);
        var inFlight = await store.TryBeginAsync(message, TestContext!.CancellationToken).ConfigureAwait(false);
        clock.Advance(TimeSpan.FromSeconds(6));
        var reclaimed = await store.TryBeginAsync(message, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => first.Should().BeTrue());
        MSTestAssertions.That(() => inFlight.Should().BeFalse());
        MSTestAssertions.That(() => reclaimed.Should().BeTrue());
    }

    [TestMethod]
    public async Task Prune_RemovesOldProcessedAndExpiredPendingRowsAsync() {
        // Arrange
        var now = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualClock(now);
        var store = new InMemoryInboxStore(clock);

        await store.TryBeginAsync(new InboxMessage("done-old", now, now.AddMinutes(5)), TestContext!.CancellationToken).ConfigureAwait(false);
        await store.CompleteAsync("done-old", null, null, now, TestContext!.CancellationToken).ConfigureAwait(false);
        await store.TryBeginAsync(new InboxMessage("done-new", now, now.AddMinutes(5)), TestContext!.CancellationToken).ConfigureAwait(false);
        await store.CompleteAsync("done-new", null, null, now.AddHours(2), TestContext!.CancellationToken).ConfigureAwait(false);
        await store.TryBeginAsync(new InboxMessage("pending-old", now, now.AddSeconds(10)), TestContext!.CancellationToken).ConfigureAwait(false);
        await store.TryBeginAsync(new InboxMessage("pending-new", now, now.AddHours(2)), TestContext!.CancellationToken).ConfigureAwait(false);

        clock.Advance(TimeSpan.FromMinutes(30));

        // Act: retention is one hour, pending timeout is one minute.
        var pruned = await store.PruneAsync(now.AddHours(1), now.AddMinutes(1), TestContext!.CancellationToken).ConfigureAwait(false);
        var oldDone = await store.GetAsync("done-old", null, TestContext!.CancellationToken).ConfigureAwait(false);
        var oldPending = await store.GetAsync("pending-old", null, TestContext!.CancellationToken).ConfigureAwait(false);
        var newDone = await store.GetAsync("done-new", null, TestContext!.CancellationToken).ConfigureAwait(false);
        var newPending = await store.GetAsync("pending-new", null, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => pruned.Should().Be(2));
        MSTestAssertions.That(() => oldDone.Should().BeNull());
        MSTestAssertions.That(() => oldPending.Should().BeNull());
        MSTestAssertions.That(() => newDone.Should().NotBeNull());
        MSTestAssertions.That(() => newPending.Should().NotBeNull());
    }

    [TestMethod]
    public async Task SameMessageId_InDifferentTenants_IsIndependentAsync() {
        // Arrange
        var now = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var store = new InMemoryInboxStore(new ManualClock(now));

        // Act
        var first = await store.TryBeginAsync(new InboxMessage("m-1", now, now.AddMinutes(5), "tenant-a"), TestContext!.CancellationToken).ConfigureAwait(false);
        var second = await store.TryBeginAsync(new InboxMessage("m-1", now, now.AddMinutes(5), "tenant-b"), TestContext!.CancellationToken).ConfigureAwait(false);
        await store.CompleteAsync("m-1", "tenant-a", null, now, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => first.Should().BeTrue());
        MSTestAssertions.That(() => second.Should().BeTrue());
        MSTestAssertions.That(() => store.IsProcessedAsync("m-1", "tenant-a", TestContext!.CancellationToken).GetAwaiter().GetResult().Should().BeTrue());
        MSTestAssertions.That(() => store.IsProcessedAsync("m-1", "tenant-b", TestContext!.CancellationToken).GetAwaiter().GetResult().Should().BeFalse());
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void Advance(TimeSpan delta) => _now += delta;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            throw new NotSupportedException("The in-memory inbox only reads the current time.");
    }
}
