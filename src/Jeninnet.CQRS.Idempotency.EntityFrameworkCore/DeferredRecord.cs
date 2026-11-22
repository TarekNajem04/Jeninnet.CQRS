//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>
/// A staged deferred command: JSON payload plus the assembly-qualified command type
/// needed to rebuild the envelope on restart. One row per deferral.
/// </summary>
public sealed class DeferredRecord : ITenantOwned {
    /// <summary>Gets or sets the tenant id; part of the composite primary key.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the store key; composite primary key with the tenant.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the assembly-qualified command type name.</summary>
    public string CommandType { get; set; } = string.Empty;

    /// <summary>Gets or sets the JSON payload of the command.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Gets or sets the message schema version; <see langword="null"/> legacy rows mean version 1.</summary>
    public int? MessageVersion { get; set; }

    /// <summary>Gets or sets the recurring schedule key; <see langword="null"/> for one-shot deferrals.</summary>
    public string? RecurringKey { get; set; }

    /// <summary>Gets or sets the recurring cron expression when this row is a recurrence.</summary>
    public string? CronExpression { get; set; }

    /// <summary>Gets or sets the recurring schedule's time zone id.</summary>
    public string? TimeZoneId { get; set; }

    /// <summary>Gets or sets the recurring overlap policy as its enum value.</summary>
    public int? RecurringOverlap { get; set; }

    /// <summary>
    /// Gets or sets the claim-check key when the payload lives outside the row.
    /// <see langword="null"/> means <see cref="Payload"/> is inline (both shapes are readable).
    /// </summary>
    public string? ClaimCheckKey { get; set; }

    /// <summary>Gets or sets when the command becomes due, in UTC.</summary>
    public DateTimeOffset DueUtc { get; set; }

    /// <summary>Gets or sets when the entry was staged, in UTC.</summary>
    public DateTimeOffset StoredAtUtc { get; set; }

    /// <summary>Gets or sets when the entry expires, in UTC. <see langword="null"/> never expires.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the optimistic-concurrency token, bumped on every overwrite.
    /// </summary>
    public long Version { get; set; }
}
