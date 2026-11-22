//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tenancy;

/// <summary>
/// Thrown when tenant-aware dispatch runs without an ambient tenant while
/// <see cref="MultiTenancyOptions.RequireTenant"/> is enabled.
/// </summary>
public sealed class TenantRequiredException : InvalidOperationException {
    /// <summary>Initializes the exception with a descriptive message.</summary>
    public TenantRequiredException()
        : base("A tenant is required for this operation, but no ambient tenant scope is active. Resolve the tenant and call ITenantContext.BeginScope before dispatching.") {
    }
}
