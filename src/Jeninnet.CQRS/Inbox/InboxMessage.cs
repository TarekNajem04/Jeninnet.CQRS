//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Inbox;

/// <summary>
/// One inbound delivery presented to <see cref="IInboxStore"/>. The message id is the broker's
/// stable id; the expiry bounds how long a pending row blocks a redelivery after a consumer crash.
/// </summary>
/// <param name="MessageId">The broker-assigned message id.</param>
/// <param name="ReceivedAtUtc">When the consumer first accepted the delivery.</param>
/// <param name="ExpiresAtUtc">When a pending row becomes reclaimable; <see langword="null"/> never expires.</param>
/// <param name="TenantId">The tenant partition, or <see langword="null"/> for the global partition.</param>
public sealed record InboxMessage(
    string MessageId,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset? ExpiresAtUtc = null,
    string? TenantId = null);

/// <summary>
/// A stored inbox row exposed for tests and operational tooling.
/// </summary>
/// <param name="MessageId">The broker-assigned message id.</param>
/// <param name="TenantId">The tenant partition (empty for the global partition).</param>
/// <param name="ReceivedAtUtc">When the consumer first accepted the delivery.</param>
/// <param name="ProcessedAtUtc">When the handler completed; <see langword="null"/> while pending or after a crash.</param>
/// <param name="ResultHash">The optional payload hash recorded on completion.</param>
/// <param name="ExpiresAtUtc">When a pending row becomes reclaimable; <see langword="null"/> once processed.</param>
public sealed record InboxEntry(
    string MessageId,
    string TenantId,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset? ProcessedAtUtc,
    string? ResultHash,
    DateTimeOffset? ExpiresAtUtc) {
    /// <summary>Gets whether the handler completed for this message id.</summary>
    public bool IsProcessed => ProcessedAtUtc is not null;
}