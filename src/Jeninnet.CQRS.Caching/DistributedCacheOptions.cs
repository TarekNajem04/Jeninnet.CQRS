//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Caching;

/// <summary>
/// Options for <see cref="DistributedCachingBehavior{TRequest, TResponse}"/>.
/// </summary>
public sealed class DistributedCacheOptions {
    /// <summary>
    /// Gets or sets the prefix added to every <see cref="ICacheable{TResponse}.CacheKey"/>,
    /// so several applications can share one cache safely. Defaults to <c>cqrs:</c>.
    /// </summary>
    public string KeyPrefix { get; set; } = "cqrs:";

    /// <summary>
    /// Gets or sets how long cache failures are throttled before another warning is logged.
    /// Defaults to 30 seconds.
    /// </summary>
    public TimeSpan FailureWindow { get; set; } = TimeSpan.FromSeconds(30);
}
