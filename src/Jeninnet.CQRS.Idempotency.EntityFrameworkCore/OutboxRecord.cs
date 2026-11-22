//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>
/// A staged notification: JSON payload plus the assembly-qualified notification type needed to
/// rebuild the envelope for delivery after a restart.
/// </summary>
public sealed class OutboxRecord : ITenantOwned {
    /// <summary>Gets or sets the tenant id.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the store key. Primary key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the assembly-qualified notification type name.</summary>
    public string NotificationType { get; set; } = string.Empty;

    /// <summary>Gets or sets the JSON payload of the notification.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the claim-check key when the payload lives outside the row.
    /// <see langword="null"/> means <see cref="Payload"/> is inline (both shapes are readable).
    /// </summary>
    public string? ClaimCheckKey { get; set; }

    /// <summary>Gets or sets when the notification was accepted, in UTC.</summary>
    public DateTimeOffset StagedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets when the notification completed, in UTC. <see langword="null"/> while the row
    /// is active; values are written only when outbox retention is enabled.
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>Gets or sets the current relay lease owner, or <see langword="null"/> when free.</summary>
    public string? LeaseOwner { get; set; }

    /// <summary>Gets or sets when the current relay lease expires, in UTC.</summary>
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }

    /// <summary>Gets or sets how many publish attempts have failed.</summary>
    public int Attempts { get; set; }

    /// <summary>Gets or sets when the next publish attempt is due after a backoff.</summary>
    public DateTimeOffset? NextAttemptUtc { get; set; }

    /// <summary>Gets or sets the full name of the last publish exception type.</summary>
    public string? LastErrorType { get; set; }

    /// <summary>Gets or sets the last publish exception message.</summary>
    public string? LastErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the row version used by the relay's conditional lease updates. It increments
    /// on every claim/fail/release so a stale worker cannot overwrite a newer owner.
    /// </summary>
    public long Version { get; set; }
}
