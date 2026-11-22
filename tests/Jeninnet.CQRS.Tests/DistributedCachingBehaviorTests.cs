//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// Characterization tests for CQRS-29: two instances share one distributed cache, a cache
/// outage falls through to the handler with throttled warnings, and concurrent misses for one
/// key execute the handler once.
/// </summary>
[TestClass]
public class DistributedCachingBehaviorTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task SecondInstance_HitsTheSharedCacheAsync() {
        // Arrange: two independent providers over one cache.
        var cache = new FakeDistributedCache();
        var firstProvider = BuildProvider(cache, new ManualClock(), new TestLogger<CacheFailureReporter>());
        var secondProvider = BuildProvider(cache, new ManualClock(), new TestLogger<CacheFailureReporter>());
        var firstBehavior = BehaviorFrom(firstProvider);
        var secondBehavior = BehaviorFrom(secondProvider);
        var firstExecutions = 0;
        var secondExecutions = 0;

        Task<string> firstHandler(CacheableQuery _, CancellationToken __) {
            firstExecutions++;

            return Task.FromResult("from-db");
        }

        Task<string> secondHandler(CacheableQuery _, CancellationToken __) {
            secondExecutions++;

            return Task.FromResult("from-db");
        }

        // Act
        var firstResult = await firstBehavior.HandleAsync(new CacheableQuery("shared"), firstHandler, TestContext!.CancellationToken);
        var secondResult = await secondBehavior.HandleAsync(new CacheableQuery("shared"), secondHandler, TestContext!.CancellationToken);

        // Assert: the second instance never ran its handler.
        MSTestAssertions.That(() => firstResult.Should().Be("from-db"));
        MSTestAssertions.That(() => secondResult.Should().Be("from-db"));
        MSTestAssertions.That(() => firstExecutions.Should().Be(1));
        MSTestAssertions.That(() => secondExecutions.Should().Be(0));
        MSTestAssertions.That(() => cache.SetCalls.Should().Be(1));
    }

    [TestMethod]
    public async Task CacheOutage_FallsThrough_AndWarnsOncePerWindowAsync() {
        // Arrange
        var cache = new FakeDistributedCache { Fail = true };
        var clock = new ManualClock();
        var logger = new TestLogger<CacheFailureReporter>();
        var behavior = BehaviorFrom(BuildProvider(cache, clock, logger));
        var executions = 0;

        Task<string> handler(CacheableQuery _, CancellationToken __) {
            executions++;

            return Task.FromResult("fallback");
        }

        // Act
        var first = await behavior.HandleAsync(new CacheableQuery("down"), handler, TestContext!.CancellationToken);
        var second = await behavior.HandleAsync(new CacheableQuery("down"), handler, TestContext!.CancellationToken);

        // Assert: both requests succeeded through the handler, one warning.
        MSTestAssertions.That(() => first.Should().Be("fallback"));
        MSTestAssertions.That(() => second.Should().Be("fallback"));
        MSTestAssertions.That(() => executions.Should().Be(2));
        MSTestAssertions.That(() => logger.Warnings.Should().Be(1));

        // A new window reports again.
        clock.Advance(TimeSpan.FromSeconds(31));
        await behavior.HandleAsync(new CacheableQuery("down"), handler, TestContext!.CancellationToken);
        MSTestAssertions.That(() => logger.Warnings.Should().Be(2));
    }

    [TestMethod]
    public async Task Stampede_ExecutesTheHandlerOnceAsync() {
        // Arrange: the first handler call blocks until every caller has read the empty cache.
        var cache = new FakeDistributedCache();
        var behavior = BehaviorFrom(BuildProvider(cache, new ManualClock(), new TestLogger<CacheFailureReporter>()));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executions = 0;

        async Task<string> handler(CacheableQuery _, CancellationToken cancellationToken) {
            Interlocked.Increment(ref executions);
            entered.TrySetResult();
            await release.Task.ConfigureAwait(false);

            return "value";
        }

        // Act
        var first = behavior.HandleAsync(new CacheableQuery("hot"), handler, TestContext!.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = behavior.HandleAsync(new CacheableQuery("hot"), handler, TestContext!.CancellationToken);
        var third = behavior.HandleAsync(new CacheableQuery("hot"), handler, TestContext!.CancellationToken);
        await SpinUntilAsync(() => cache.GetCalls >= 3, "all callers read the cache");

        release.TrySetResult();
        var results = await Task.WhenAll(first, second, third).WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        MSTestAssertions.That(() => executions.Should().Be(1));
        MSTestAssertions.That(() => results.All(static result => result == "value").Should().BeTrue());
        MSTestAssertions.That(() => cache.SetCalls.Should().Be(1));
    }

    private static ServiceProvider BuildProvider(FakeDistributedCache cache, ManualClock clock, TestLogger<CacheFailureReporter> logger) {
        var services = new ServiceCollection();
        services.AddSingleton<IDistributedCache>(cache);
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<ILogger<CacheFailureReporter>>(logger);
        services.AddCqrsDistributedCaching();

        return services.BuildServiceProvider();
    }

    private static DistributedCachingBehavior<CacheableQuery, string> BehaviorFrom(IServiceProvider provider) =>
        provider.GetServices<IPipelineBehavior<CacheableQuery, string>>().OfType<DistributedCachingBehavior<CacheableQuery, string>>().Single();

    private static async Task SpinUntilAsync(Func<bool> condition, string what) {
        var deadline = Environment.TickCount64 + 5000;

        while(!condition()) {
            if(Environment.TickCount64 > deadline) {
                throw new TimeoutException($"timed out waiting for: {what}");
            }

            await Task.Yield();
        }
    }

    private sealed record CacheableQuery(string CacheKey) : IQuery<string>, ICacheable<string> {
        public MemoryCacheEntryOptions? CacheOptions => new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) };
    }

    private sealed class ManualClock : TimeProvider {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void Advance(TimeSpan span) => _now += span;
    }

    private sealed class TestLogger<T> : ILogger<T> {
        private readonly Lock _sync = new();
        private readonly List<LogLevel> _levels = [];

        public int Warnings {
            get {
                lock(_sync) {
                    return _levels.Count(static level => level is LogLevel.Warning);
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
            lock(_sync) {
                _levels.Add(logLevel);
            }
        }
    }

    private sealed class FakeDistributedCache : IDistributedCache {
        private readonly Lock _sync = new();
        private readonly Dictionary<string, byte[]> _entries = new(StringComparer.Ordinal);
        private int _getCalls;
        private int _setCalls;

        public bool Fail { get; set; }

        public int GetCalls {
            get {
                lock(_sync) {
                    return _getCalls;
                }
            }
        }

        public int SetCalls {
            get {
                lock(_sync) {
                    return _setCalls;
                }
            }
        }

        public byte[]? Get(string key) => GetAsync(key, default).GetAwaiter().GetResult();

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) {
            ThrowIfFailing();

            lock(_sync) {
                _getCalls++;

                return Task.FromResult(_entries.TryGetValue(key, out var value) ? value : null);
            }
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            SetAsync(key, value, options, default).GetAwaiter().GetResult();

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) {
            ThrowIfFailing();

            lock(_sync) {
                _setCalls++;
                _entries[key] = value;
            }

            return Task.CompletedTask;
        }

        public void Refresh(string key) => ThrowIfFailing();

        public Task RefreshAsync(string key, CancellationToken token = default) {
            ThrowIfFailing();

            return Task.CompletedTask;
        }

        public void Remove(string key) => RemoveAsync(key, default).GetAwaiter().GetResult();

        public Task RemoveAsync(string key, CancellationToken token = default) {
            ThrowIfFailing();

            lock(_sync) {
                _entries.Remove(key);
            }

            return Task.CompletedTask;
        }

        private void ThrowIfFailing() {
            if(Fail) {
                throw new InvalidOperationException("cache down");
            }
        }
    }
}