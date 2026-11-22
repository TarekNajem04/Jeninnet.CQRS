//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Caching;

/// <summary>
///   A pipeline behavior that attempts to serve responses from an <see cref="IMemoryCache"/>
///   and caches new responses for subsequent calls.
/// </summary>
/// <typeparam name="TRequest">
///   The request type, optionally implementing <see cref="ICacheable{TResponse}"/>.
/// </typeparam>
/// <typeparam name="TResponse">
///   The response type to be cached.
/// </typeparam>
/// <remarks>
///   Initializes a new instance of the <see cref="CachingBehavior{TRequest,TResponse}"/> class.
/// </remarks>
/// <param name="cache">
///   The <see cref="IMemoryCache"/> used to store and retrieve cached responses.
/// </param>
/// <param name="tenantKeys">
///   Tenant-qualified key composer; <see langword="null"/> when multi-tenancy is not registered.
/// </param>
/// <exception cref="ArgumentNullException">
///   Thrown if <paramref name="cache"/> is <see langword="null"/>.
/// </exception>
public sealed class CachingBehavior<TRequest, TResponse>(IMemoryCache cache, ITenantScopedKey? tenantKeys = null) : IPipelineBehavior<TRequest, TResponse> {
    private readonly IMemoryCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly ITenantScopedKey? _tenantKeys = tenantKeys;

    /// <inheritdoc/>
    public Task<TResponse> HandleAsync(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResponse>> next,
        CancellationToken cancellationToken) {
        // Only cache if the request implements ICacheable<TResponse>
        if(request is ICacheable<TResponse> cacheable) {
            var key = _tenantKeys?.Compose(cacheable.CacheKey) ?? cacheable.CacheKey;

            // Try to get from cache
            if(_cache.TryGetValue(key, out TResponse? cachedValue) && cachedValue is not null) {
                return Task.FromResult(cachedValue);
            }

            // Not in cache: invoke the rest of the pipeline
            return CacheAndReturnAsync(cacheable, key, next, cancellationToken);
        }

        // Not a cacheable request: continue pipeline unmodified
        return next(request, cancellationToken);
    }

    private async Task<TResponse> CacheAndReturnAsync(
        ICacheable<TResponse> cacheable,
        string key,
        Func<TRequest, CancellationToken, Task<TResponse>> next,
        CancellationToken cancellationToken) {
        // Execute downstream behaviors and handler
        var response = await next((TRequest)cacheable, cancellationToken)
                            .ConfigureAwait(false);

        // Store in cache
        var options = cacheable.CacheOptions ?? new MemoryCacheEntryOptions();
        _cache.Set(key, response, options);

        return response;
    }
}
