//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

[TestClass]
public class CachingBehaviorTests {
    [TestMethod]
    public async Task HandleAsync_ShouldOnlyInvokeHandlerOnce_WhenKeyIsCachedAsync() {
        // Arrange
        var services = new ServiceCollection();
        services.AddMemoryCache();
        var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IMemoryCache>();

        var behavior = new CachingBehavior<CacheableTestQuery, string>(cache);
        var query = new CacheableTestQuery("Order_123");
        var executionCount = 0;

        // This delegate simulates the next behavior or final handler
        Task<string> next(CacheableTestQuery _, CancellationToken __) {
            executionCount++;
            return Task.FromResult("DatabaseResult");
        }

        // Act
        var firstResult = await behavior.HandleAsync(query, next, default);
        var secondResult = await behavior.HandleAsync(query, next, default);

        // Assert
        MSTestAssertions.That(() => firstResult.Should().Be("DatabaseResult"));
        MSTestAssertions.That(() => secondResult.Should().Be("DatabaseResult"));
        MSTestAssertions.That(() => executionCount.Should().Be(1, "The handler should have been skipped on the second call."));
    }
}

public record CacheableTestQuery(string CacheKey) : IQuery<string>, ICacheable<string> {
    public MemoryCacheEntryOptions? CacheOptions => new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) };
}
