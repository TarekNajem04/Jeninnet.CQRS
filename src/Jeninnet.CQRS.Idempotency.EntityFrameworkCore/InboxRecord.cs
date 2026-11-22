//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>
/// One processed inbound message id. The composite primary key is <c>(TenantId, MessageId)</c>;
/// a processed row is retained for duplicate detection, and a pending row is reclaimable once
/// <see cref="ExpiresAtUtc"/> passes.
/// </summary>
public sealed class InboxRecord : ITenantOwned {
    /// <summary>Gets or sets the tenant id; part of the composite primary key.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the broker-assigned message id; composite primary key with the tenant.</summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>Gets or sets when the consumer first accepted the delivery, in UTC.</summary>
    public DateTimeOffset ReceivedAtUtc { get; set; }

    /// <summary>Gets or sets when the handler completed, in UTC; <see langword="null"/> while pending.</summary>
    public DateTimeOffset? ProcessedAtUtc { get; set; }

    /// <summary>Gets or sets the optional SHA-256 of the inbound payload for audits.</summary>
    public string? ResultHash { get; set; }

    /// <summary>Gets or sets when a pending row becomes reclaimable, in UTC.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    /// <summary>Gets or sets the optimistic-concurrency token, bumped on every claim or completion.</summary>
    public long Version { get; set; }
}