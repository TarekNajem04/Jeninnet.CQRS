//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Caching;

/// <summary>
///   Marks a query as cacheable by supplying a cache key and entry options.
/// </summary>
/// <typeparam name="TResponse">
///   The type of the response value that will be cached.
/// </typeparam>
public interface ICacheable<out TResponse> {
    /// <summary>
    ///   A unique key used to store/retrieve the response in the cache.
    /// </summary>
    string CacheKey { get; }

    /// <summary>
    ///   The <see cref="MemoryCacheEntryOptions"/> to apply
    ///   when storing the response. If <see langword="null"/>, default options are used.
    /// </summary>
    MemoryCacheEntryOptions? CacheOptions { get; }
}
