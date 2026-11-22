//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Resilience;

/// <summary>
/// A pipeline behavior that sheds upstream load through a fixed-window rate limiter partitioned
/// by key (request type by default). Requests without an available permit fail fast with
/// <see cref="RateLimitRejectedException"/> instead of collapsing the handler.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
/// Initializes a new instance with the given limiter and options.
/// </remarks>
/// <param name="limiter">The shared partitioned limiter. Never <see langword="null"/>.</param>
/// <param name="options">The rate-limit options. Never <see langword="null"/>.</param>
/// <param name="tenantKeys">Tenant-qualified key composer; <see langword="null"/> when multi-tenancy is not registered.</param>
public sealed class RateLimitBehavior<TRequest, TResponse>(
    PartitionedRateLimiter<string> limiter,
    IOptions<RateLimitOptions> options,
    ITenantScopedKey? tenantKeys = null
) : IPipelineBehavior<TRequest, TResponse> {
    private static readonly string _defaultKey = typeof(TRequest).FullName ?? typeof(TRequest).Name;

    private readonly PartitionedRateLimiter<string> _limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
    private readonly Func<object, string>? _keySelector = (options ?? throw new ArgumentNullException(nameof(options))).Value.KeySelector;
    private readonly ITenantScopedKey? _tenantKeys = tenantKeys;

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(TRequest request, Func<TRequest, CancellationToken, Task<TResponse>> next, CancellationToken cancellationToken) {
        // No boxing on the default path: the key is cached per closed generic.
        var key = _keySelector is null ? _defaultKey : _keySelector(request!);
        key = _tenantKeys?.Compose(key) ?? key;

        using var lease = await _limiter.AcquireAsync(key, 1, cancellationToken).ConfigureAwait(false);

        if(!lease.IsAcquired) {
            throw new RateLimitRejectedException(key);
        }

        return await next(request, cancellationToken).ConfigureAwait(false);
    }
}