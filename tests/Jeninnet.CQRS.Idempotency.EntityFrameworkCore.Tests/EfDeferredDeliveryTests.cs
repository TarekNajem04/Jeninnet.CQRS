//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore.Tests;

/// <summary>
/// Characterization tests for CQRS-18/20: the deferred durability log survives
/// restarts, replays through the pump, drops unresolvable types, and skips expired entries.
/// </summary>
[TestClass]
public class EfDeferredDeliveryTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task EfDeferred_CompleteTryStageAndPruneBranchesAsync() {
        // Arrange
        var file = NewDbFile();
        var calls = new List<string>();
        await using var provider = BuildHost(file, calls);
        var store = provider.GetRequiredService<IDeferredStore>();
        var efStore = (EfDeferredStore)store;
        var cancellationToken = TestContext!.CancellationToken;
        var now = DateTimeOffset.UtcNow;

        // Missing completes are a no-op; nothing is expired yet.
        await store.CompleteAsync("missing", cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(0, await store.PruneExpiredAsync(now, cancellationToken).ConfigureAwait(false));

        // A deterministic key is staged once and can be completed.
        var envelope = new DeferredEnvelope<DeferredPing>(new DeferredPing("once"), now, null);
        Assert.IsTrue(await efStore.TryStageAsync("deterministic", envelope, cancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await efStore.TryStageAsync("deterministic", envelope, cancellationToken).ConfigureAwait(false));
        Assert.AreEqual(1, (await store.ListAsync(cancellationToken).ConfigureAwait(false)).Count);
        await store.CompleteAsync("deterministic", cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(0, (await store.ListAsync(cancellationToken).ConfigureAwait(false)).Count);

        // Expired rows prune; live rows survive.
        Assert.IsTrue(await efStore.TryStageAsync(
            "expired",
            new DeferredEnvelope<DeferredPing>(new DeferredPing("old"), now.AddDays(-2), now.AddDays(-1)),
            cancellationToken).ConfigureAwait(false));
        Assert.IsTrue(await efStore.TryStageAsync(
            "live",
            new DeferredEnvelope<DeferredPing>(new DeferredPing("live"), now, now.AddDays(1)),
            cancellationToken).ConfigureAwait(false));

        var pruned = await store.PruneExpiredAsync(now, cancellationToken).ConfigureAwait(false);
        var remaining = await store.ListAsync(cancellationToken).ConfigureAwait(false);

        // Assert
        Assert.AreEqual(1, pruned);
        Assert.AreEqual(1, remaining.Count);
        Assert.AreEqual("live", remaining[0].Key);
    }

    private static string NewDbFile() => Path.Combine(Path.GetTempPath(), $"cqrs18-{Guid.NewGuid():N}.db");

    private static ServiceProvider BuildHost(string file, List<string> calls) {
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddTransient<ICommandHandler<DeferredPing, Unit>, DeferredPingHandler>();
        services.AddCqrsEfDeferredDelivery(builder => builder.UseSqlite($"Data Source={file}"));
        var provider = services.BuildServiceProvider();

        using(var scope = provider.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>().CreateDbContext();
            db.Database.EnsureCreated();
            db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            db.Database.ExecuteSqlRaw("PRAGMA busy_timeout=10000;");
        }

        return provider;
    }

    private static async Task SpinUntilAsync(Func<bool> condition, string what, int timeoutMilliseconds = 5000) {
        var sw = Stopwatch.StartNew();

        while(!condition()) {
            if(sw.ElapsedMilliseconds > timeoutMilliseconds) {
                break;
            }

            await Task.Delay(25).ConfigureAwait(false);
        }

        MSTestAssertions.That(() => condition().Should().BeTrue($"timed out waiting for: {what}"));
    }

    [TestMethod]
    public async Task Restart_ReplaysStagedCommand_ExactlyOnceAsync() {
        // Arrange: stage without starting the pump, then drop the whole host.
        // Short real-time due: the pump waits on the real clock here.
        var file = NewDbFile();
        var calls = new List<string>();
        await using(var first = BuildHost(file, calls)) {
            var dispatcher = first.GetRequiredService<IDeferredDispatcher>();
            await dispatcher.DeferAsync(new DeferredPing("Hi"), TimeSpan.FromMilliseconds(200), TestContext!.CancellationToken);
            MSTestAssertions.That(() => calls.Should().BeEmpty());
        }

        // Act: a fresh host over the same file recovers and runs the entry.
        var replayed = new List<string>();
        await using var second = BuildHost(file, replayed);
        var secondDispatcher = second.GetRequiredService<IDeferredDispatcher>();
        await ((IHostedService)secondDispatcher).StartAsync(TestContext!.CancellationToken);

        try {
            var store = second.GetRequiredService<IDeferredStore>();
            await SpinUntilAsync(() => replayed.Count == 1, "recovered command");
            MSTestAssertions.That(() => replayed.Should().Equal(["Hi"]));

            // Assert: completed, so a second pump pass cannot replay it.
            await SpinUntilAsync(() => store.ListAsync(TestContext!.CancellationToken).GetAwaiter().GetResult().Count == 0, "store drained");
            await Task.Delay(300, TestContext!.CancellationToken).ConfigureAwait(false);
            MSTestAssertions.That(() => replayed.Should().HaveCount(1));
        }
        finally {
            await ((IHostedService)secondDispatcher).StopAsync(default).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task UnresolvableType_IsDeadLettered_NotReplayedAsync() {
        // Arrange: a row whose command type no longer exists.
        var file = NewDbFile();
        var calls = new List<string>();
        await using var provider = BuildHost(file, calls);
        var factory = provider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>();
        await using(var db = await factory.CreateDbContextAsync(TestContext!.CancellationToken)) {
            db.DeferredCommands.Add(new DeferredRecord {
                Key = "ghost",
                CommandType = "Missing.Command, MissingAssembly",
                Payload = "{}",
                DueUtc = DateTimeOffset.UtcNow,
                StoredAtUtc = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(TestContext!.CancellationToken);
        }

        // Act: listing surfaces the unresolved row; the pump dead-letters it instead of
        // replaying it forever or dropping an accepted message silently.
        var store = provider.GetRequiredService<IDeferredStore>();
        var deadLetters = provider.GetRequiredService<IDeadLetterStore>();
        var dispatcher = provider.GetRequiredService<IDeferredDispatcher>();
        await ((IHostedService)dispatcher).StartAsync(TestContext!.CancellationToken);

        try {
            await SpinUntilAsync(() => store.ListAsync(TestContext!.CancellationToken).GetAwaiter().GetResult().Count == 0, "ghost completed");
            await SpinUntilAsync(() => deadLetters.ListAsync(TestContext!.CancellationToken).GetAwaiter().GetResult().Count == 1, "ghost dead-lettered");

            // Assert
            var entry = (await deadLetters.ListAsync(TestContext!.CancellationToken)).Single();
            var replayed = await dispatcher.ReplayAsync(entry.Key, TestContext!.CancellationToken);
            MSTestAssertions.That(() => calls.Should().BeEmpty());
            MSTestAssertions.That(() => entry.LastErrorMessage.Should().Contain("Missing.Command"));
            MSTestAssertions.That(() => replayed.Should().BeFalse("an unresolved command has nothing to re-stage"));
        }
        finally {
            await ((IHostedService)dispatcher).StopAsync(default).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task ExpiredEntry_NeverRunsAsync() {
        // Arrange: already-expired at staging time. Staged before StartAsync so the
        // recovery path picks it up (the pump only learns through the queue).
        var file = NewDbFile();
        var calls = new List<string>();
        await using var provider = BuildHost(file, calls);
        var store = provider.GetRequiredService<IDeferredStore>();
        await store.StageAsync(new DeferredEnvelope<DeferredPing>(new DeferredPing("stale"), DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(-1)), TestContext!.CancellationToken);
        var dispatcher = provider.GetRequiredService<IDeferredDispatcher>();
        await ((IHostedService)dispatcher).StartAsync(TestContext!.CancellationToken);

        try {
            // Act: the pump skips the expired entry and completes it.
            await SpinUntilAsync(() => store.ListAsync(TestContext!.CancellationToken).GetAwaiter().GetResult().Count == 0, "expired completed");

            // Assert
            MSTestAssertions.That(() => calls.Should().BeEmpty());
        }
        finally {
            await ((IHostedService)dispatcher).StopAsync(default).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task ConcurrentStages_AllPersistAsync() {
        // Arrange
        var file = NewDbFile();
        var calls = new List<string>();
        await using var provider = BuildHost(file, calls);
        var store = provider.GetRequiredService<IDeferredStore>();
        var due = DateTimeOffset.UtcNow.AddHours(1);

        // Act: eight concurrent stagers, none may surface an exception.
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => store.StageAsync(new DeferredEnvelope<DeferredPing>(new DeferredPing($"m{i}"), due, null), TestContext!.CancellationToken)));

        // Assert
        var listed = await store.ListAsync(TestContext!.CancellationToken);
        MSTestAssertions.That(() => listed.Should().HaveCount(8));
    }

    private sealed record DeferredPing(string Name) : ICommand;

    private sealed class DeferredPingHandler(List<string> calls) : ICommandHandler<DeferredPing, Unit> {
        public Task<Unit> HandleAsync(DeferredPing command, CancellationToken cancellationToken) {
            calls.Add(command.Name);
            return Task.FromResult(Unit.Value);
        }
    }
}