//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>Archived copy of a processed consumer-inbox row, with the archival timestamp.</summary>
public sealed class InboxArchiveRecord : ITenantOwned {
    /// <summary>Gets or sets the tenant id.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the broker-assigned message id.</summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>Gets or sets when the consumer first accepted the delivery, in UTC.</summary>
    public DateTimeOffset ReceivedAtUtc { get; set; }

    /// <summary>Gets or sets when the handler completed, in UTC.</summary>
    public DateTimeOffset? ProcessedAtUtc { get; set; }

    /// <summary>Gets or sets the optional payload hash retained for audits.</summary>
    public string? ResultHash { get; set; }

    /// <summary>Gets or sets the original pending expiry, in UTC.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    /// <summary>Gets or sets the original optimistic-concurrency version.</summary>
    public long Version { get; set; }

    /// <summary>Gets or sets when the row moved to the archive, in UTC.</summary>
    public DateTimeOffset ArchivedAtUtc { get; set; }
}

/// <summary>Archived copy of a dead-letter row, with the archival timestamp.</summary>
public sealed class DeadLetterArchiveRecord : ITenantOwned {
    /// <summary>Gets or sets the tenant id.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the original dead-letter key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the assembly-qualified command type name.</summary>
    public string CommandType { get; set; } = string.Empty;

    /// <summary>Gets or sets the inline JSON payload, when it lived in the row.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Gets or sets the claim-check key, when the payload lived outside the row.</summary>
    public string? ClaimCheckKey { get; set; }

    /// <summary>Gets or sets the original due time, in UTC.</summary>
    public DateTimeOffset DueUtc { get; set; }

    /// <summary>Gets or sets the original expiry, in UTC.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    /// <summary>Gets or sets how many executions failed.</summary>
    public int Attempts { get; set; }

    /// <summary>Gets or sets the last failure type name.</summary>
    public string LastErrorType { get; set; } = string.Empty;

    /// <summary>Gets or sets the last failure message.</summary>
    public string LastErrorMessage { get; set; } = string.Empty;

    /// <summary>Gets or sets when the first failure was observed, in UTC.</summary>
    public DateTimeOffset FirstFailedAtUtc { get; set; }

    /// <summary>Gets or sets when the last failure was observed, in UTC.</summary>
    public DateTimeOffset LastFailedAtUtc { get; set; }

    /// <summary>Gets or sets when the row moved to the archive, in UTC.</summary>
    public DateTimeOffset ArchivedAtUtc { get; set; }
}

/// <summary>Archived copy of a terminal background job, with the archival timestamp.</summary>
public sealed class JobArchiveRecord : ITenantOwned {
    /// <summary>Gets or sets the job id.</summary>
    public string JobId { get; set; } = string.Empty;

    /// <summary>Gets or sets the assembly-qualified job payload type name.</summary>
    public string JobType { get; set; } = string.Empty;

    /// <summary>Gets or sets the serialized job payload.</summary>
    public byte[] Payload { get; set; } = [];

    /// <summary>Gets or sets the tenant id.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the terminal status as an integer.</summary>
    public int Status { get; set; }

    /// <summary>Gets or sets the final progress, 0–100.</summary>
    public int Progress { get; set; }

    /// <summary>Gets or sets how many attempts started.</summary>
    public int Attempts { get; set; }

    /// <summary>Gets or sets the attempt budget.</summary>
    public int MaxAttempts { get; set; }

    /// <summary>Gets or sets when the job was submitted, in UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Gets or sets when the row last changed, in UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>Gets or sets when the next attempt was due, in UTC.</summary>
    public DateTimeOffset NextAttemptUtc { get; set; }

    /// <summary>Gets or sets when the first attempt started, in UTC.</summary>
    public DateTimeOffset? StartedAtUtc { get; set; }

    /// <summary>Gets or sets when the job reached a terminal state, in UTC.</summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>Gets or sets the final worker lease owner.</summary>
    public string? LeaseOwner { get; set; }

    /// <summary>Gets or sets the final lease expiry, in UTC.</summary>
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }

    /// <summary>Gets or sets whether cancellation was requested.</summary>
    public bool CancellationRequested { get; set; }

    /// <summary>Gets or sets the last failure type name.</summary>
    public string? LastErrorType { get; set; }

    /// <summary>Gets or sets the last failure message.</summary>
    public string? LastErrorMessage { get; set; }

    /// <summary>Gets or sets the original optimistic-concurrency version.</summary>
    public long Version { get; set; }

    /// <summary>Gets or sets when the row moved to the archive, in UTC.</summary>
    public DateTimeOffset ArchivedAtUtc { get; set; }
}

/// <summary>Archived copy of a completed notification-outbox row, with the archival timestamp.</summary>
public sealed class OutboxArchiveRecord : ITenantOwned {
    /// <summary>Gets or sets the tenant id.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the original outbox key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the assembly-qualified notification type name.</summary>
    public string NotificationType { get; set; } = string.Empty;

    /// <summary>Gets or sets the inline JSON payload, when it lived in the row.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Gets or sets the claim-check key, when the payload lived outside the row.</summary>
    public string? ClaimCheckKey { get; set; }

    /// <summary>Gets or sets when the notification was accepted, in UTC.</summary>
    public DateTimeOffset StagedAtUtc { get; set; }

    /// <summary>Gets or sets the original relay lease owner.</summary>
    public string? LeaseOwner { get; set; }

    /// <summary>Gets or sets the original relay lease expiry, in UTC.</summary>
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }

    /// <summary>Gets or sets the number of failed publish attempts.</summary>
    public int Attempts { get; set; }

    /// <summary>Gets or sets the next retry time from before completion, in UTC.</summary>
    public DateTimeOffset? NextAttemptUtc { get; set; }

    /// <summary>Gets or sets the last publish failure type name.</summary>
    public string? LastErrorType { get; set; }

    /// <summary>Gets or sets the last publish failure message.</summary>
    public string? LastErrorMessage { get; set; }

    /// <summary>Gets or sets the original optimistic-concurrency version.</summary>
    public long Version { get; set; }

    /// <summary>Gets or sets when the row completed, in UTC.</summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>Gets or sets when the row moved to the archive, in UTC.</summary>
    public DateTimeOffset ArchivedAtUtc { get; set; }
}