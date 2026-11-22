//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Scheduling;

/// <summary>Whether a scheduled message fires once or repeats from a cron expression.</summary>
public enum ScheduledMessageKind {
    /// <summary>Fires once at <c>NextRunUtc</c> (delayed or absolute one-shot).</summary>
    OneShot = 0,

    /// <summary>Fires at <c>NextRunUtc</c>, then schedules its next cron occurrence.</summary>
    Recurring = 1,
}

/// <summary>One persisted scheduler row. Payload bytes are produced by the registered serializer.</summary>
/// <param name="Key">The stable schedule key.</param>
/// <param name="MessageType">The assembly-qualified message type.</param>
/// <param name="ContentType">The payload media type.</param>
/// <param name="Payload">The serialized message.</param>
/// <param name="Kind">One-shot or recurring.</param>
/// <param name="NextRunUtc">The next due instant in UTC.</param>
/// <param name="CreatedAtUtc">When the schedule was created.</param>
/// <param name="CronExpression">The five-field cron expression for recurring rows.</param>
/// <param name="TimeZoneId">The time zone id for recurring rows.</param>
/// <param name="Attempts">Failed publish attempts so far.</param>
/// <param name="Version">The optimistic-concurrency version.</param>
/// <param name="TenantId">The tenant that owns the schedule, or <see langword="null"/>.</param>
public sealed record ScheduledMessage(
    string Key,
    string MessageType,
    string ContentType,
    byte[] Payload,
    ScheduledMessageKind Kind,
    DateTimeOffset NextRunUtc,
    DateTimeOffset CreatedAtUtc,
    string? CronExpression,
    string? TimeZoneId,
    int Attempts,
    long Version,
    string? TenantId = null);

/// <summary>Per-schedule options.</summary>
public sealed class ScheduledMessageOptions {
    /// <summary>
    /// Gets or sets an explicit stable key. A recurring schedule requires one; a one-shot without
    /// a key gets a generated id. An existing key is treated as already scheduled.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>Gets or sets the cron time zone; defaults to UTC.</summary>
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Utc;
}

/// <summary>One leased due row, returned by the store.</summary>
/// <param name="Message">The schedule snapshot.</param>
/// <param name="Owner">The worker that owns the lease.</param>
/// <param name="LeaseExpiresAtUtc">When the lease expires.</param>
public sealed record SchedulerLease(ScheduledMessage Message, string Owner, DateTimeOffset LeaseExpiresAtUtc);