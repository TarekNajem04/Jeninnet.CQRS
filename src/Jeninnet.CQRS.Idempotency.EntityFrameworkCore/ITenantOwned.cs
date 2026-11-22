//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>
/// A row owned by one tenant. The shared <see cref="IdempotencyDbContext"/> applies a query filter
/// over this property so a scoped read can never observe another tenant's rows.
/// </summary>
public interface ITenantOwned {
    /// <summary>Gets or sets the tenant id; empty for non-tenant deployments.</summary>
    string TenantId { get; set; }
}