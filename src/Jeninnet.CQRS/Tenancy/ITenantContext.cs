//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tenancy;

/// <summary>
/// Ambient tenant for the current async flow. The host opens a scope per request or per consumed
/// message; every dispatch, cache key, idempotency key, and outbound envelope reads this value.
/// </summary>
public interface ITenantContext {
    /// <summary>Gets the current tenant id, or <see langword="null"/> outside a tenant scope.</summary>
    string? Current { get; }

    /// <summary>
    /// Opens a tenant scope. Scopes nest: disposing restores the tenant that was current before
    /// the call. The value flows across <see langword="await"/> and into child tasks.
    /// </summary>
    /// <param name="tenantId">The tenant id; must not be empty.</param>
    /// <returns>A scope that restores the previous tenant.</returns>
    IDisposable BeginScope(string tenantId);
}
