//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore.Tests;

/// <summary>
/// CQRS-57 durable consumer inbox: rows round-trip through SQLite, the second delivery of an id is
/// a duplicate across contexts, an abandoned claim can be retried, tenants stay isolated, and
/// pruning honors both windows.
/// </summary>
[TestClass]
public sealed class EfInboxStoreTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task EfInbox_BeginsCompletesAndBlocksDuplicatesAsync() {
        // Arrange
        var file = Path.Combine(Path.GetTempPath(), $"cqrs57-inbox-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddCqrsEfInbox(db => db.UseSqlite($"Data Source={file}"));

        await using var provider = services.BuildServiceProvider();

        using(var scope = provider.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>().CreateDbContext();
            db.Database.EnsureCreated();
        }

        var store = provider.GetRequiredService<IInboxStore>();
        var now = DateTimeOffset.UtcNow;
        var message = new InboxMessage("message-1", now, now.AddMinutes(5));

        // Act
        var first = await store.TryBeginAsync(message, TestContext!.CancellationToken).ConfigureAwait(false);
        var duplicate = await store.TryBeginAsync(message, TestContext!.CancellationToken).ConfigureAwait(false);
        await store.CompleteAsync("message-1", null, "hash-1", now.AddSeconds(1), TestContext!.CancellationToken).ConfigureAwait(false);
        var after = await store.TryBeginAsync(message, TestContext!.CancellationToken).ConfigureAwait(false);
        var entry = await store.GetAsync("message-1", null, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => store.Should().BeOfType<EfInboxStore>());
        MSTestAssertions.That(() => first.Should().BeTrue());
        MSTestAssertions.That(() => duplicate.Should().BeFalse());
        MSTestAssertions.That(() => after.Should().BeFalse());
        MSTestAssertions.That(() => entry.Should().NotBeNull());
        MSTestAssertions.That(() => entry!.IsProcessed.Should().BeTrue());
        MSTestAssertions.That(() => entry!.ResultHash.Should().Be("hash-1"));
    }

    [TestMethod]
    public async Task EfInbox_AbandonAllowsRetryAndPruneRemovesOldRowsAsync() {
        // Arrange
        var file = Path.Combine(Path.GetTempPath(), $"cqrs57-prune-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddCqrsEfInbox(db => db.UseSqlite($"Data Source={file}"));

        await using var provider = services.BuildServiceProvider();

        using(var scope = provider.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>().CreateDbContext();
            db.Database.EnsureCreated();
        }

        var store = provider.GetRequiredService<IInboxStore>();
        var now = DateTimeOffset.UtcNow;

        // Act: abandon makes an immediate retry possible.
        await store.TryBeginAsync(new InboxMessage("abandoned", now, now.AddMinutes(5)), TestContext!.CancellationToken).ConfigureAwait(false);
        await store.AbandonAsync("abandoned", null, TestContext!.CancellationToken).ConfigureAwait(false);
        var retry = await store.TryBeginAsync(new InboxMessage("abandoned", now, now.AddMinutes(5)), TestContext!.CancellationToken).ConfigureAwait(false);

        // Arrange: one old processed row, one expired pending row, and one fresh row.
        await store.TryBeginAsync(new InboxMessage("old", now.AddDays(-10), now.AddDays(-9)), TestContext!.CancellationToken).ConfigureAwait(false);
        await store.CompleteAsync("old", null, null, now.AddDays(-10), TestContext!.CancellationToken).ConfigureAwait(false);
        await store.TryBeginAsync(new InboxMessage("stale", now.AddDays(-10), now.AddMinutes(-9)), TestContext!.CancellationToken).ConfigureAwait(false);
        await store.TryBeginAsync(new InboxMessage("fresh", now, now.AddMinutes(5)), TestContext!.CancellationToken).ConfigureAwait(false);
        await store.CompleteAsync("fresh", null, null, now, TestContext!.CancellationToken).ConfigureAwait(false);

        // Act
        var pruned = await store.PruneAsync(now.AddDays(-1), now.AddMinutes(-1), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => retry.Should().BeTrue());
        MSTestAssertions.That(() => pruned.Should().Be(2));
        MSTestAssertions.That(() => store.GetAsync("old", null, TestContext!.CancellationToken).GetAwaiter().GetResult().Should().BeNull());
        MSTestAssertions.That(() => store.GetAsync("stale", null, TestContext!.CancellationToken).GetAwaiter().GetResult().Should().BeNull());
        MSTestAssertions.That(() => store.GetAsync("fresh", null, TestContext!.CancellationToken).GetAwaiter().GetResult().Should().NotBeNull());
    }

    [TestMethod]
    public async Task EfInbox_SameIdInDifferentTenants_IsIndependentAsync() {
        // Arrange
        var file = Path.Combine(Path.GetTempPath(), $"cqrs57-tenant-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddCqrsEfInbox(db => db.UseSqlite($"Data Source={file}"));

        await using var provider = services.BuildServiceProvider();

        using(var scope = provider.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>().CreateDbContext();
            db.Database.EnsureCreated();
        }

        var store = provider.GetRequiredService<IInboxStore>();
        var now = DateTimeOffset.UtcNow;

        // Act
        var first = await store.TryBeginAsync(new InboxMessage("message-1", now, now.AddMinutes(5), "tenant-a"), TestContext!.CancellationToken).ConfigureAwait(false);
        var second = await store.TryBeginAsync(new InboxMessage("message-1", now, now.AddMinutes(5), "tenant-b"), TestContext!.CancellationToken).ConfigureAwait(false);
        await store.CompleteAsync("message-1", "tenant-a", null, now, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => first.Should().BeTrue());
        MSTestAssertions.That(() => second.Should().BeTrue());
        MSTestAssertions.That(() => store.IsProcessedAsync("message-1", "tenant-a", TestContext!.CancellationToken).GetAwaiter().GetResult().Should().BeTrue());
        MSTestAssertions.That(() => store.IsProcessedAsync("message-1", "tenant-b", TestContext!.CancellationToken).GetAwaiter().GetResult().Should().BeFalse());
    }
}