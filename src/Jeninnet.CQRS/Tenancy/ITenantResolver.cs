//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tenancy;

/// <summary>
/// Resolves the tenant for an incoming request or message. The host calls the resolver inside its
/// own middleware or consumer and opens an <see cref="ITenantContext"/> scope with the result.
/// </summary>
public interface ITenantResolver {
    /// <summary>Resolves the current tenant, or <see langword="null"/> when none applies.</summary>
    /// <param name="services">The request-scoped provider.</param>
    /// <returns>The tenant id, or <see langword="null"/>.</returns>
    string? Resolve(IServiceProvider services);
}

/// <summary>Delegate form of <see cref="ITenantResolver"/> for hosts that do not need a class.</summary>
/// <param name="services">The request-scoped provider.</param>
/// <returns>The tenant id, or <see langword="null"/>.</returns>
public delegate string? TenantResolver(IServiceProvider services);

/// <summary>Adapts a <see cref="TenantResolver"/> delegate to <see cref="ITenantResolver"/>.</summary>
/// <param name="resolver">The delegate. Never <see langword="null"/>.</param>
public sealed class DelegateTenantResolver(TenantResolver resolver) : ITenantResolver {
    private readonly TenantResolver _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

    /// <inheritdoc />
    public string? Resolve(IServiceProvider services) => _resolver(services);
}