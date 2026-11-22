//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Caching;

/// <summary>
/// A pipeline behavior that serves <see cref="ICacheable{TResponse}"/> requests from an
/// <see cref="IDistributedCache"/>, so every instance behind the same cache shares responses.
/// </summary>
/// <remarks>
/// <para>
/// Payloads go through <see cref="IMessageSerializer"/> (JSON by default). Cache reads, writes,
/// and serialization failures are treated as misses: the handler runs and the request succeeds.
/// Warnings are throttled by <see cref="CacheFailureReporter"/>.
/// </para>
/// <para>
/// Concurrent misses for one key are single-flighted in-process: the first caller runs the
/// handler, the rest wait and re-check the cache. This prevents a per-instance stampede; it is
/// not a cross-instance lock.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The request type, optionally implementing <see cref="ICacheable{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The response type to be cached.</typeparam>
/// <param name="cache">The distributed cache supplied by the host. Never <see langword="null"/>.</param>
/// <param name="serializer">The payload serializer. Never <see langword="null"/>.</param>
/// <param name="guard">The in-process single-flight guard. Never <see langword="null"/>.</param>
/// <param name="failures">The throttled failure reporter. Never <see langword="null"/>.</param>
/// <param name="options">The key-prefix and failure-window options. Never <see langword="null"/>.</param>
/// <param name="tenantKeys">Tenant-qualified key composer; <see langword="null"/> when multi-tenancy is not registered.</param>
public sealed class DistributedCachingBehavior<TRequest, TResponse>(
    IDistributedCache cache,
    IMessageSerializer serializer,
    CacheStampedeGuard guard,
    CacheFailureReporter failures,
    IOptions<DistributedCacheOptions> options,
    ITenantScopedKey? tenantKeys = null) : IPipelineBehavior<TRequest, TResponse> {
    private readonly IDistributedCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly IMessageSerializer _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    private readonly CacheStampedeGuard _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    private readonly CacheFailureReporter _failures = failures ?? throw new ArgumentNullException(nameof(failures));
    private readonly DistributedCacheOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    private readonly ITenantScopedKey? _tenantKeys = tenantKeys;

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResponse>> next,
        CancellationToken cancellationToken) {
        if(request is not ICacheable<TResponse> cacheable) {
            return await next(request, cancellationToken).ConfigureAwait(false);
        }

        var cacheKey = _tenantKeys?.Compose(cacheable.CacheKey) ?? cacheable.CacheKey;
        var key = _options.KeyPrefix + cacheKey;
        var cached = await TryReadAsync(key, cancellationToken).ConfigureAwait(false);

        if(cached.Found) {
            return cached.Value;
        }

        using var lease = await _guard.EnterAsync(key, cancellationToken).ConfigureAwait(false);

        // Another caller may have filled the cache while this one waited for the turn.
        cached = await TryReadAsync(key, cancellationToken).ConfigureAwait(false);

        if(cached.Found) {
            return cached.Value;
        }

        var response = await next(request, cancellationToken).ConfigureAwait(false);

        await TryWriteAsync(key, response, cacheable.CacheOptions, cancellationToken).ConfigureAwait(false);

        return response;
    }

    private async Task<(bool Found, TResponse Value)> TryReadAsync(string key, CancellationToken cancellationToken) {
        try {
            var payload = await _cache.GetAsync(key, cancellationToken).ConfigureAwait(false);

            if(payload is null) {
                return (false, default!);
            }

            var value = _serializer.Deserialize(payload, typeof(TResponse));

            return value is null ? (false, default!) : (true, (TResponse)value);
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch(Exception exception) {
            _failures.Report(exception);

            return (false, default!);
        }
    }

    private async Task TryWriteAsync(string key, TResponse response, MemoryCacheEntryOptions? cacheOptions, CancellationToken cancellationToken) {
        if(response is null) {
            return; // Nothing useful to cache; a null hit is indistinguishable from a miss.
        }

        try {
            var payload = _serializer.Serialize(response, typeof(TResponse));
            await _cache.SetAsync(key, payload, ToDistributed(cacheOptions), cancellationToken).ConfigureAwait(false);
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch(Exception exception) {
            _failures.Report(exception);
        }
    }

    private static DistributedCacheEntryOptions ToDistributed(MemoryCacheEntryOptions? options) {
        var distributed = new DistributedCacheEntryOptions();

        if(options is null) {
            return distributed;
        }

        if(options.AbsoluteExpirationRelativeToNow is { } relative) {
            distributed.AbsoluteExpirationRelativeToNow = relative;
        } else if(options.AbsoluteExpiration is { } absolute) {
            distributed.AbsoluteExpiration = absolute;
        }

        if(options.SlidingExpiration is { } sliding) {
            distributed.SlidingExpiration = sliding;
        }

        return distributed;
    }
}
