//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Benchmarks;

/// <summary>
/// In-memory cache hit through <see cref="CachingBehavior{TRequest, TResponse}"/>: the handler
/// must never run, so this measures the hit path only.
/// </summary>
[MemoryDiagnoser]
public class CacheHitBenchmarks {
    private MemoryCache _cache = null!;
    private CachingBehavior<CacheablePing, string> _behavior = null!;
    private readonly CacheablePing _query = new();

    [GlobalSetup]
    public void Setup() {
        _cache = new MemoryCache(new MemoryCacheOptions());
        _cache.Set(_query.CacheKey, "pong");
        _behavior = new CachingBehavior<CacheablePing, string>(_cache);
    }

    [GlobalCleanup]
    public void Cleanup() => _cache.Dispose();

    [Benchmark]
    public Task<string> CacheHit() => _behavior.HandleAsync(_query, Miss, default);

    private static Task<string> Miss(CacheablePing query, CancellationToken cancellationToken) =>
        Task.FromResult("miss");

    public sealed record CacheablePing : IQuery<string>, ICacheable<string> {
        public string CacheKey => "bench:cache:ping";

        public MemoryCacheEntryOptions? CacheOptions => null;
    }
}