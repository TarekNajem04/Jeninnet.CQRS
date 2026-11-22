//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore.Tests;

/// <summary>
/// Characterization tests for CQRS-27 on the EF Core store: a staged notification survives a
/// restart, is rebuilt from its persisted type and payload, and is delivered exactly once by
/// the dispatcher in that process.
/// </summary>
[TestClass]
public class EfOutboxStoreTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task EfOutbox_GuardAndLeaseOwnershipBranchesAsync() {
        // Arrange
        var file = NewDbFile();
        var now = DateTimeOffset.UtcNow;
        var cancellationToken = TestContext!.CancellationToken;
        await using var host = BuildHost(file, []);
        var store = host.GetRequiredService<IOutboxStore>();
        var key = await store.StageAsync(
            new OutboxNotification<OutboxTemplate>(new OutboxTemplate("guards"), now),
            cancellationToken).ConfigureAwait(false);

        // Act + Assert: argument guards.
        _ = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => store.LeaseAsync("worker", 0, TimeSpan.FromMinutes(1), now, cancellationToken)).ConfigureAwait(false);
        _ = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => store.LeaseAsync("worker", 1, TimeSpan.Zero, now, cancellationToken)).ConfigureAwait(false);
        _ = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => store.FailLeaseAsync(key, "worker", new InvalidOperationException("boom"), 0, now, now, cancellationToken)).ConfigureAwait(false);

        // Missing or unowned rows are no-ops.
        Assert.IsFalse(await store.CompleteLeaseAsync("missing", "worker", now, cancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await store.CompleteLeaseAsync(key, "other", now, cancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await store.FailLeaseAsync("missing", "worker", new InvalidOperationException("boom"), 1, now, now, cancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await store.ReleaseLeaseAsync("missing", "worker", now, cancellationToken).ConfigureAwait(false));

        // A lease can only be released or failed by its owner.
        var leases = await store.LeaseAsync("worker", 10, TimeSpan.FromMinutes(1), now, cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(1, leases.Count);
        Assert.IsFalse(await store.ReleaseLeaseAsync(key, "other", now, cancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await store.FailLeaseAsync(key, "other", new InvalidOperationException("boom"), 1, now, now, cancellationToken).ConfigureAwait(false));
        Assert.IsTrue(await store.ReleaseLeaseAsync(key, "worker", now, cancellationToken).ConfigureAwait(false));

        // The unleased completion path removes the row.
        await store.CompleteAsync(key, cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(0, (await store.ListAsync(cancellationToken).ConfigureAwait(false)).Count);
    }

    private static string NewDbFile() => Path.Combine(Path.GetTempPath(), $"cqrs27-{Guid.NewGuid():N}.db");

    private static ServiceProvider BuildHost(string file, List<string> calls) {
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddTransient<INotificationHandler<OutboxTemplate>, RecordingNotificationHandler>();
        services.AddCqrsEfNotificationOutbox(builder => builder.UseSqlite($"Data Source={file}"));
        var provider = services.BuildServiceProvider();

        using(var scope = provider.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>().CreateDbContext();
            db.Database.EnsureCreated();
        }

        return provider;
    }

    [TestMethod]
    public async Task PendingNotification_SurvivesRestart_AndIsDeliveredAsync() {
        // Arrange: stage, then drop the whole host without starting the dispatcher.
        var file = NewDbFile();

        await using(var first = BuildHost(file, [])) {
            var store = first.GetRequiredService<IOutboxStore>();
            await store.StageAsync(
                new OutboxNotification<OutboxTemplate>(new OutboxTemplate("hello"), DateTimeOffset.UtcNow),
                TestContext!.CancellationToken);
        }

        // Act: a fresh host over the same file finds and delivers it.
        var calls = new List<string>();
        await using var second = BuildHost(file, calls);
        var restartedStore = second.GetRequiredService<IOutboxStore>();
        var pending = await restartedStore.ListAsync(TestContext!.CancellationToken);
        var dispatcher = second.GetServices<IHostedService>().OfType<OutboxDispatcher>().Single();
        var delivered = await dispatcher.DispatchPendingAsync(TestContext!.CancellationToken);

        // Assert: reconstructed notification, delivered once, then completed.
        MSTestAssertions.That(() => pending.Should().HaveCount(1));
        MSTestAssertions.That(() => ((OutboxTemplate)pending[0].Notification.Command).Name.Should().Be("hello"));
        MSTestAssertions.That(() => delivered.Should().Be(1));
        MSTestAssertions.That(() => calls.Should().Equal(["hello"]));
        MSTestAssertions.That(() => restartedStore.ListAsync(TestContext!.CancellationToken).GetAwaiter().GetResult().Should().BeEmpty());
    }

    [TestMethod]
    public async Task Lease_RebuildsNotificationAcrossRestart_AndCompletesTheRowAsync() {
        // Arrange: one row staged by a disposed host.
        var file = NewDbFile();
        var now = DateTimeOffset.UtcNow;

        await using(var first = BuildHost(file, [])) {
            await first.GetRequiredService<IOutboxStore>().StageAsync(
                new OutboxNotification<OutboxTemplate>(new OutboxTemplate("leased"), now),
                TestContext!.CancellationToken);
        }

        // Act: a fresh host leases the row.
        await using var second = BuildHost(file, []);
        var store = second.GetRequiredService<IOutboxStore>();
        var leases = await store.LeaseAsync("worker", 10, TimeSpan.FromSeconds(30), now, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert: the payload crossed the restart, then the owner removes it.
        MSTestAssertions.That(() => leases.Should().HaveCount(1));
        MSTestAssertions.That(() => ((OutboxTemplate)leases[0].Envelope.Command).Name.Should().Be("leased"));
        MSTestAssertions.That(() => leases[0].Attempts.Should().Be(0));

        var completed = await store.CompleteLeaseAsync(leases[0].Key, "worker", now, TestContext!.CancellationToken).ConfigureAwait(false);

        MSTestAssertions.That(() => completed.Should().BeTrue());
        MSTestAssertions.That(() => store.ListAsync(TestContext!.CancellationToken).GetAwaiter().GetResult().Should().BeEmpty());
    }

    [TestMethod]
    public async Task Lease_IsExclusiveAcrossHosts_AndBacksOffFailedRowsAsync() {
        // Arrange: two rows and two stores over one SQLite file.
        var file = NewDbFile();
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string firstKey;

        await using var first = BuildHost(file, []);
        var firstStore = first.GetRequiredService<IOutboxStore>();
        firstKey = await firstStore.StageAsync(
            new OutboxNotification<OutboxTemplate>(new OutboxTemplate("first"), now),
            TestContext!.CancellationToken).ConfigureAwait(false);
        await firstStore.StageAsync(
            new OutboxNotification<OutboxTemplate>(new OutboxTemplate("second"), now),
            TestContext!.CancellationToken).ConfigureAwait(false);

        await using var second = BuildHost(file, []);
        var secondStore = second.GetRequiredService<IOutboxStore>();

        // Act: the first owner leases both rows; the second owner sees none while the head is leased.
        var leased = await firstStore.LeaseAsync("worker-a", 10, TimeSpan.FromSeconds(30), now, TestContext!.CancellationToken).ConfigureAwait(false);
        var blocked = await secondStore.LeaseAsync("worker-b", 10, TimeSpan.FromSeconds(30), now, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => leased.Should().HaveCount(2));
        MSTestAssertions.That(() => blocked.Should().BeEmpty());

        // Act: the head fails and backs off; the second owner must wait for it.
        var failed = await firstStore.FailLeaseAsync(firstKey, "worker-a", new InvalidOperationException("boom"), 1, now.AddSeconds(2), now, TestContext!.CancellationToken).ConfigureAwait(false);
        var stillBlocked = await secondStore.LeaseAsync("worker-b", 10, TimeSpan.FromSeconds(30), now, TestContext!.CancellationToken).ConfigureAwait(false);
        var afterBackoff = await secondStore.LeaseAsync("worker-b", 10, TimeSpan.FromSeconds(30), now.AddSeconds(2), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert: the retry is the head, and attempts survived the store boundary.
        MSTestAssertions.That(() => failed.Should().BeTrue());
        MSTestAssertions.That(() => stillBlocked.Should().BeEmpty());
        MSTestAssertions.That(() => afterBackoff.Should().HaveCount(1));
        MSTestAssertions.That(() => afterBackoff[0].Key.Should().Be(firstKey));
        MSTestAssertions.That(() => afterBackoff[0].Attempts.Should().Be(1));
    }

    [TestMethod]
    public async Task Dispatcher_SecondPass_FindsNothingAsync() {
        // Arrange
        await using var provider = BuildHost(NewDbFile(), []);
        var store = provider.GetRequiredService<IOutboxStore>();
        await store.StageAsync(
            new OutboxNotification<OutboxTemplate>(new OutboxTemplate("once"), DateTimeOffset.UtcNow),
            TestContext!.CancellationToken);
        var dispatcher = provider.GetServices<IHostedService>().OfType<OutboxDispatcher>().Single();

        // Act
        var first = await dispatcher.DispatchPendingAsync(TestContext!.CancellationToken);
        var second = await dispatcher.DispatchPendingAsync(TestContext!.CancellationToken);

        // Assert: completion is durable, so a second pass is a no-op.
        MSTestAssertions.That(() => first.Should().Be(1));
        MSTestAssertions.That(() => second.Should().Be(0));
    }

    private sealed record OutboxTemplate(string Name) : INotification;

    private sealed class RecordingNotificationHandler(List<string> calls) : INotificationHandler<OutboxTemplate> {
        public Task HandleAsync(OutboxTemplate notification, CancellationToken cancellationToken) {
            calls.Add(notification.Name);

            return Task.CompletedTask;
        }
    }
}
