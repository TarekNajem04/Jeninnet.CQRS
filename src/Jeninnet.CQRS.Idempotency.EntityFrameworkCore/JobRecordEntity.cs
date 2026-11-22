//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>
/// One durable background job: typed payload, status, progress, attempts, lease, and last error.
/// The row lives beside the other durability tables and is leased optimistically by the worker.
/// </summary>
public sealed class JobRecordEntity : ITenantOwned {
    /// <summary>Gets or sets the job id. Primary key.</summary>
    public string JobId { get; set; } = string.Empty;

    /// <summary>Gets or sets the assembly-qualified job payload type name.</summary>
    public string JobType { get; set; } = string.Empty;

    /// <summary>Gets or sets the serialized job payload.</summary>
    public byte[] Payload { get; set; } = [];

    /// <summary>Gets or sets the tenant that submitted the job; empty for the global partition.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the lifecycle status as an integer.</summary>
    public int Status { get; set; }

    /// <summary>Gets or sets the last reported progress, 0–100.</summary>
    public int Progress { get; set; }

    /// <summary>Gets or sets how many attempts have started.</summary>
    public int Attempts { get; set; }

    /// <summary>Gets or sets the attempt budget captured on submit.</summary>
    public int MaxAttempts { get; set; }

    /// <summary>Gets or sets when the job was submitted, in UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Gets or sets when the row last changed, in UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>Gets or sets when the next attempt is due, in UTC.</summary>
    public DateTimeOffset NextAttemptUtc { get; set; }

    /// <summary>Gets or sets when the first attempt started, in UTC.</summary>
    public DateTimeOffset? StartedAtUtc { get; set; }

    /// <summary>Gets or sets when the job reached a terminal state, in UTC.</summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>Gets or sets the worker lease owner while running.</summary>
    public string? LeaseOwner { get; set; }

    /// <summary>Gets or sets when the running lease expires, in UTC.</summary>
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }

    /// <summary>Gets or sets whether cooperative cancellation was requested.</summary>
    public bool CancellationRequested { get; set; }

    /// <summary>Gets or sets the last failure type name.</summary>
    public string? LastErrorType { get; set; }

    /// <summary>Gets or sets the last failure message.</summary>
    public string? LastErrorMessage { get; set; }

    /// <summary>Gets or sets the optimistic-concurrency version.</summary>
    public long Version { get; set; }
}
