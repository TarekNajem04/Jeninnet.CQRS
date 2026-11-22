//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>
/// A single stored idempotent response: key, response payload, and expiry.
/// </summary>
public sealed class IdempotencyRecord : ITenantOwned {
    /// <summary>Gets or sets the tenant id; part of the composite primary key.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the caller-supplied idempotency key; composite primary key with the tenant.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the assembly-qualified name of the stored response type.</summary>
    public string ResponseType { get; set; } = string.Empty;

    /// <summary>Gets or sets the JSON payload of the stored response.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the claim-check key when the payload lives outside the row.
    /// <see langword="null"/> means <see cref="Payload"/> is inline (both shapes are readable).
    /// </summary>
    public string? ClaimCheckKey { get; set; }

    /// <summary>Gets or sets when the entry was stored, in UTC.</summary>
    public DateTimeOffset StoredAtUtc { get; set; }

    /// <summary>Gets or sets when the entry expires, in UTC. <see langword="null"/> never expires.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the owner holding the execution lease; <see langword="null"/> when the row is
    /// free or completed.
    /// </summary>
    public string? LeaseOwner { get; set; }

    /// <summary>
    /// Gets or sets when the execution lease expires, in UTC. An expired lease is reclaimable by
    /// the next caller; <see langword="null"/> when no lease is held.
    /// </summary>
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the optimistic-concurrency token, bumped on every overwrite.
    /// The store increments it; concurrent overwrites retry instead of losing silently.
    /// </summary>
    public long Version { get; set; }
}
