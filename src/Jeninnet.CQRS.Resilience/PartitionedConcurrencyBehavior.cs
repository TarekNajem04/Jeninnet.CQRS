//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Resilience;

/// <summary>
/// Serializes dispatches that share a partition key while letting different keys proceed in
/// parallel. The key is the request type by default, or a custom selector from
/// <see cref="PartitionedConcurrencyOptions.KeySelector"/>.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <param name="registry">The shared partition registry. Never <see langword="null"/>.</param>
/// <param name="options">The partitioning options. Never <see langword="null"/>.</param>
/// <param name="tenantKeys">Tenant-qualified key composer; <see langword="null"/> when multi-tenancy is not registered.</param>
public sealed class PartitionedConcurrencyBehavior<TRequest, TResponse>(
    PartitionedConcurrencyRegistry registry,
    IOptions<PartitionedConcurrencyOptions> options,
    ITenantScopedKey? tenantKeys = null
) : IPipelineBehavior<TRequest, TResponse> {
    private static readonly string _defaultKey = typeof(TRequest).FullName ?? typeof(TRequest).Name;

    private readonly PartitionedConcurrencyRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly Func<object, string>? _keySelector = (options ?? throw new ArgumentNullException(nameof(options))).Value.KeySelector;
    private readonly ITenantScopedKey? _tenantKeys = tenantKeys;

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(TRequest request, Func<TRequest, CancellationToken, Task<TResponse>> next, CancellationToken cancellationToken) {
        // No boxing on the default path: the key is cached per closed generic.
        var key = _keySelector is null ? _defaultKey : _keySelector(request!);
        key = _tenantKeys?.Compose(key) ?? key;

        using var lease = await _registry.AcquireAsync(key, cancellationToken).ConfigureAwait(false);

        return await next(request, cancellationToken).ConfigureAwait(false);
    }
}
