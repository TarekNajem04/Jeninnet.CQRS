//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore.Tests;

/// <summary>
/// Store-contract tests for CQRS-11 against a real SQLite file (not mocks):
/// round trip, overwrite, expiry, concurrent duplicate stores, and behavior replay.
/// </summary>
[TestClass]
public class EfIdempotencyStoreTests {
    private static string NewDbFile() => Path.Combine(Path.GetTempPath(), $"cqrs11-{Guid.NewGuid():N}.db");

    private static ServiceProvider BuildHost(string file, ManualTimeProvider clock, Action<EfIdempotencyStoreOptions>? configure = null) {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(new DbFilePath(file));
        services.AddDbContextFactory<IdempotencyDbContext>(static (provider, builder) =>
            builder.UseSqlite($"Data Source={provider.GetRequiredService<DbFilePath>().Path}"));
        services.AddCqrsEfIdempotencyStore(configureStore: configure);

        return services.BuildServiceProvider();
    }

    private static async Task PrepareDatabaseAsync(IServiceProvider provider) {
        var factory = provider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>();
        await using var db = await factory.CreateDbContextAsync().ConfigureAwait(false);
        await db.Database.EnsureCreatedAsync().ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;").ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=10000;").ConfigureAwait(false);
    }

    [TestMethod]
    public async Task RoundTrip_SurvivesRestartAsync() {
        // Arrange: one file, two host instances (restart = new provider on the same file).
        var file = NewDbFile();
        var clock = new ManualTimeProvider();
        await using(var first = BuildHost(file, clock)) {
            await PrepareDatabaseAsync(first).ConfigureAwait(false);
            await first.GetRequiredService<IIdempotencyStore>().StoreAsync("order-1", "hello", default).ConfigureAwait(false);
        }

        // Act: a fresh host over the same file must still find the entry.
        await using var second = BuildHost(file, clock);
        var stored = await second.GetRequiredService<IIdempotencyStore>().GetAsync<string>("order-1", default).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => stored.Should().Be("hello"));
    }

    [TestMethod]
    public async Task Overwrite_ReturnsLatestAsync() {
        // Arrange
        var clock = new ManualTimeProvider();
        await using var provider = BuildHost(NewDbFile(), clock);
        await PrepareDatabaseAsync(provider).ConfigureAwait(false);
        var store = provider.GetRequiredService<IIdempotencyStore>();

        // Act
        await store.StoreAsync("order-1", "v1", default).ConfigureAwait(false);
        await store.StoreAsync("order-1", "v2", default).ConfigureAwait(false);
        var stored = await store.GetAsync<string>("order-1", default).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => stored.Should().Be("v2"));
    }

    [TestMethod]
    public async Task ExpiredEntry_ReturnsMiss_AndPrunedAsync() {
        // Arrange
        var clock = new ManualTimeProvider();
        await using var provider = BuildHost(NewDbFile(), clock, static options => options.TimeToLive = TimeSpan.FromHours(1));
        await PrepareDatabaseAsync(provider).ConfigureAwait(false);
        var store = provider.GetRequiredService<IIdempotencyStore>();
        await store.StoreAsync("order-1", "hello", default).ConfigureAwait(false);

        // Act: move past the TTL. Prune first (the row is still there), then read.
        clock.Advance(TimeSpan.FromHours(2));
        var pruned = await ((EfIdempotencyStore)store).PruneExpiredAsync(default).ConfigureAwait(false);
        var stored = await store.GetAsync<string>("order-1", default).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => pruned.Should().Be(1));
        MSTestAssertions.That(() => stored.Should().BeNull());

        // Act: a second entry exercises the read-time prune (Get deletes, later prune finds nothing).
        await store.StoreAsync("order-2", "hello", default).ConfigureAwait(false);
        clock.Advance(TimeSpan.FromHours(2));
        var reread = await store.GetAsync<string>("order-2", default).ConfigureAwait(false);
        var repruned = await ((EfIdempotencyStore)store).PruneExpiredAsync(default).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => reread.Should().BeNull());
        MSTestAssertions.That(() => repruned.Should().Be(0));
    }

    [TestMethod]
    public async Task ConcurrentStores_ConvergeWithoutErrorAsync() {
        // Arrange
        var clock = new ManualTimeProvider();
        await using var provider = BuildHost(NewDbFile(), clock);
        await PrepareDatabaseAsync(provider).ConfigureAwait(false);
        var store = provider.GetRequiredService<IIdempotencyStore>();

        // Act: eight writers race on the same key; none may surface an exception.
        var values = Enumerable.Range(0, 8).Select(static i => $"v{i}").ToList();
        await Task.WhenAll(values.Select(value => store.StoreAsync("order-1", value, default))).ConfigureAwait(false);
        var stored = await store.GetAsync<string>("order-1", default).ConfigureAwait(false);

        // Assert: one winner, and it is one of the writers.
        MSTestAssertions.That(() => values.Should().Contain(stored!));
    }

    [TestMethod]
    public async Task TypeMismatch_ReturnsMissAsync() {
        // Arrange
        var clock = new ManualTimeProvider();
        await using var provider = BuildHost(NewDbFile(), clock);
        await PrepareDatabaseAsync(provider).ConfigureAwait(false);
        var store = provider.GetRequiredService<IIdempotencyStore>();
        await store.StoreAsync("order-1", "hello", default).ConfigureAwait(false);

        // Act
        var stored = await store.GetAsync<int>("order-1", default).ConfigureAwait(false);

        // Assert: same miss semantics as the in-memory store.
        MSTestAssertions.That(() => stored.Should().Be(0));
    }

    [TestMethod]
    public async Task Behavior_ReplaysStoredResponse_WithoutHandlerAsync() {
        // Arrange
        var clock = new ManualTimeProvider();
        await using var provider = BuildHost(NewDbFile(), clock);
        await PrepareDatabaseAsync(provider).ConfigureAwait(false);
        var behavior = new IdempotencyBehavior<IdempotentPing, string>(provider.GetRequiredService<IIdempotencyStore>());
        var executions = 0;
        Task<string> next(IdempotentPing _, CancellationToken __) {
            executions++;
            return Task.FromResult("pong");
        }

        // Act
        var first = await behavior.HandleAsync(new IdempotentPing("k1"), next, default).ConfigureAwait(false);
        var second = await behavior.HandleAsync(new IdempotentPing("k1"), next, default).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => first.Should().Be("pong"));
        MSTestAssertions.That(() => second.Should().Be("pong"));
        MSTestAssertions.That(() => executions.Should().Be(1));
    }

    private sealed record IdempotentPing(string Key) : IIdempotentCommand<string> {
        public string IdempotencyKey => Key;
    }

    private sealed class DbFilePath(string Path) {
        public string Path { get; } = Path;
    }

    private sealed class ManualTimeProvider : TimeProvider {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void Advance(TimeSpan span) => _now += span;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new NopTimer();

        private sealed class NopTimer : ITimer {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
