//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>One durable schedule row: message payload, next run, lease, and retry metadata.</summary>
public sealed class ScheduledMessageRecord : ITenantOwned {
    /// <summary>Gets or sets the tenant id; part of the composite primary key.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the stable schedule key; composite primary key with the tenant.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the assembly-qualified message type.</summary>
    public string MessageType { get; set; } = string.Empty;

    /// <summary>Gets or sets the payload content type.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Gets or sets the serialized message.</summary>
    public byte[] Payload { get; set; } = [];

    /// <summary>Gets or sets 0 for one-shot or 1 for recurring.</summary>
    public int Kind { get; set; }

    /// <summary>Gets or sets the next due instant, in UTC.</summary>
    public DateTimeOffset NextRunUtc { get; set; }

    /// <summary>Gets or sets the next due instant as ticks for portable indexed due scans.</summary>
    public long NextRunTicks { get; set; }

    /// <summary>Gets or sets when the schedule was created, in UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Gets or sets when the row last changed, in UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>Gets or sets the five-field cron expression for recurring rows.</summary>
    public string? CronExpression { get; set; }

    /// <summary>Gets or sets the cron time zone id for recurring rows.</summary>
    public string? TimeZoneId { get; set; }

    /// <summary>Gets or sets failed publish attempts.</summary>
    public int Attempts { get; set; }

    /// <summary>Gets or sets the optimistic-concurrency version.</summary>
    public long Version { get; set; }

    /// <summary>Gets or sets the current lease owner, or null.</summary>
    public string? LeaseOwner { get; set; }

    /// <summary>Gets or sets when the lease expires, in UTC.</summary>
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }

    /// <summary>Gets or sets the lease expiry as ticks for portable indexed scans.</summary>
    public long? LeaseExpiresTicks { get; set; }

    /// <summary>Gets or sets the last publish error type.</summary>
    public string? LastErrorType { get; set; }

    /// <summary>Gets or sets the last publish error message.</summary>
    public string? LastErrorMessage { get; set; }
}
