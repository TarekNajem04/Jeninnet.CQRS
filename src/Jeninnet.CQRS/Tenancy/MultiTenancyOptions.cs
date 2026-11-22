//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tenancy;

/// <summary>
/// Options for tenant-aware dispatch. When tenancy is registered, a missing tenant is a bug: the
/// default is strict so a request can never silently read or write another tenant's partition.
/// </summary>
public sealed class MultiTenancyOptions {
    /// <summary>
    /// Gets or sets whether a missing ambient tenant throws <see cref="TenantRequiredException"/>.
    /// Defaults to <see langword="true"/>; set to <see langword="false"/> for optional tenancy.
    /// </summary>
    public bool RequireTenant { get; set; } = true;

    /// <summary>
    /// Gets or sets the separator between the tenant and the underlying key. Defaults to a colon.
    /// </summary>
    public string KeySeparator { get; set; } = ":";
}
