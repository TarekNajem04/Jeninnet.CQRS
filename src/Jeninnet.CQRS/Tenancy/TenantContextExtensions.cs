//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tenancy;

/// <summary>Helpers shared by dispatch, transport, outbox, and scheduler tenant propagation.</summary>
public static class TenantContextExtensions {
    /// <summary>
    /// Resolves the tenant for a dispatch boundary. An explicit tenant wins; otherwise the ambient
    /// tenant is used. In strict mode a missing tenant throws
    /// <see cref="TenantRequiredException"/> instead of silently falling back to a global partition.
    /// </summary>
    /// <param name="context">The ambient context, or <see langword="null"/> when tenancy is not registered.</param>
    /// <param name="options">The tenancy options.</param>
    /// <param name="explicitTenant">A tenant already stored on the message, when available.</param>
    /// <returns>The tenant id, or <see langword="null"/> when optional tenancy has none.</returns>
    public static string? ResolveTenant(this ITenantContext? context, MultiTenancyOptions options, string? explicitTenant = null) {
        Guard.NotNull(options);

        if(!StringCompat.IsNullOrWhiteSpace(explicitTenant)) {
            return explicitTenant.Trim();
        }

        var current = context?.Current;

        if(!StringCompat.IsNullOrWhiteSpace(current)) {
            return current.Trim();
        }

        if(options.RequireTenant) {
            throw new TenantRequiredException();
        }

        return null;
    }

    /// <summary>
    /// Opens a tenant scope when <paramref name="tenantId"/> is present. Returns
    /// <see langword="null"/> when there is no tenant or no context, so callers can use
    /// <c>using var scope = ...</c> unconditionally.
    /// </summary>
    /// <param name="context">The ambient context, or <see langword="null"/>.</param>
    /// <param name="tenantId">The tenant stored on the message, or <see langword="null"/>.</param>
    /// <returns>The scope, or <see langword="null"/>.</returns>
    public static IDisposable? EnterScope(this ITenantContext? context, string? tenantId) =>
        context is null || StringCompat.IsNullOrWhiteSpace(tenantId) ? null : context.BeginScope(tenantId);
}
