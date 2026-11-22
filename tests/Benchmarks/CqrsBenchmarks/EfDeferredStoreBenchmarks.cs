//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Benchmarks;

/// <summary>
/// EF Core SQLite deferred store round trip: stage, list, and complete one command.
/// </summary>
[MemoryDiagnoser]
public class EfDeferredStoreBenchmarks {
    private string _file = null!;
    private BenchDbContextFactory _factory = null!;
    private EfDeferredStore _store = null!;
    private DeferredEnvelope<Ping> _envelope = null!;

    [GlobalSetup]
    public void Setup() {
        _file = Path.Combine(Path.GetTempPath(), $"cqrs-bench-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<IdempotencyDbContext>()
            .UseSqlite($"Data Source={_file}")
            .Options;
        _factory = new BenchDbContextFactory(options);

        using(var db = _factory.CreateDbContext()) {
            db.Database.EnsureCreated();
        }

        _store = new EfDeferredStore(
            _factory,
            Options.Create(new EfDeferredStoreOptions()),
            new JsonMessageSerializer(Options.Create(new JsonMessageSerializerOptions())),
            new ClaimCheckPolicy(Options.Create(new ClaimCheckOptions()), []),
            new MessageUpgradePolicy());
        _envelope = new DeferredEnvelope<Ping>(new Ping("bench"), DateTimeOffset.UtcNow, null);
    }

    [GlobalCleanup]
    public void Cleanup() {
        try {
            File.Delete(_file);
        }
        catch(IOException) {
            // A lingering SQLite handle keeps the temp file; it is outside the repo.
        }
    }

    [Benchmark]
    public async Task StageListComplete() {
        var key = await _store.StageAsync(_envelope, default).ConfigureAwait(false);
        _ = await _store.ListAsync(default).ConfigureAwait(false);
        await _store.CompleteAsync(key, default).ConfigureAwait(false);
    }

    public sealed record Ping(string Name) : ICommand;

    private sealed class BenchDbContextFactory(DbContextOptions<IdempotencyDbContext> options) : IDbContextFactory<IdempotencyDbContext> {
        public IdempotencyDbContext CreateDbContext() => new(options);

        public Task<IdempotencyDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}