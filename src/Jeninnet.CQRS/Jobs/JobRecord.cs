//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Jobs;

/// <summary>The lifecycle of a background job.</summary>
public enum JobStatus {
    /// <summary>Waiting for a worker and a due retry.</summary>
    Queued = 0,

    /// <summary>A worker holds the lease and is executing the handler.</summary>
    Running = 1,

    /// <summary>The handler completed successfully.</summary>
    Succeeded = 2,

    /// <summary>The handler exhausted its attempts.</summary>
    Failed = 3,

    /// <summary>A cancellation was requested and the worker stopped the job.</summary>
    Canceled = 4,
}

/// <summary>
/// Durable job row: the typed payload, lifecycle state, progress, attempts, lease, and the last
/// error. Terminal rows are immutable; only a worker holding the lease may change a running row.
/// </summary>
public sealed record JobRecord {
    /// <summary>Gets the job id returned to the submitter.</summary>
    public string JobId { get; init; } = string.Empty;

    /// <summary>Gets the assembly-qualified job payload type name.</summary>
    public string JobType { get; init; } = string.Empty;

    /// <summary>Gets the serialized job payload.</summary>
    public byte[] Payload { get; init; } = [];

    /// <summary>Gets the tenant that submitted the job, when any.</summary>
    public string? TenantId { get; init; }

    /// <summary>Gets the lifecycle status.</summary>
    public JobStatus Status { get; init; }

    /// <summary>Gets the last reported progress, 0–100.</summary>
    public int Progress { get; init; }

    /// <summary>Gets how many times a worker has attempted the job.</summary>
    public int Attempts { get; init; }

    /// <summary>Gets the attempt budget captured when the job was submitted.</summary>
    public int MaxAttempts { get; init; }

    /// <summary>Gets when the job was submitted, in UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Gets when the row last changed, in UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; init; }

    /// <summary>Gets when the job may next be leased, in UTC.</summary>
    public DateTimeOffset NextAttemptUtc { get; init; }

    /// <summary>Gets when the first attempt started, in UTC.</summary>
    public DateTimeOffset? StartedAtUtc { get; init; }

    /// <summary>Gets when the job reached a terminal state, in UTC.</summary>
    public DateTimeOffset? CompletedAtUtc { get; init; }

    /// <summary>Gets the worker lease owner while running.</summary>
    public string? LeaseOwner { get; init; }

    /// <summary>Gets when the running lease expires, in UTC.</summary>
    public DateTimeOffset? LeaseExpiresAtUtc { get; init; }

    /// <summary>Gets whether the submitter requested cooperative cancellation.</summary>
    public bool CancellationRequested { get; init; }

    /// <summary>Gets the last failure exception type name.</summary>
    public string? LastErrorType { get; init; }

    /// <summary>Gets the last failure exception message.</summary>
    public string? LastErrorMessage { get; init; }

    /// <summary>Gets the optimistic-concurrency version.</summary>
    public long Version { get; init; }

    /// <summary>Gets whether the status is terminal.</summary>
    public bool IsTerminal => Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Canceled;
}

/// <summary>The public status returned by <see cref="IJobClient"/>.</summary>
/// <param name="JobId">The job id.</param>
/// <param name="JobType">The job payload type name.</param>
/// <param name="Status">The lifecycle status.</param>
/// <param name="Progress">The last reported progress, 0–100.</param>
/// <param name="Attempts">Attempts made so far.</param>
/// <param name="MaxAttempts">The attempt budget.</param>
/// <param name="CancellationRequested">Whether cancellation was requested.</param>
/// <param name="LastErrorType">The last failure type, when any.</param>
/// <param name="LastErrorMessage">The last failure message, when any.</param>
/// <param name="CreatedAtUtc">When the job was submitted.</param>
/// <param name="UpdatedAtUtc">When the row last changed.</param>
/// <param name="NextAttemptUtc">When the next attempt is due.</param>
/// <param name="StartedAtUtc">When the first attempt started.</param>
/// <param name="CompletedAtUtc">When the job reached a terminal state.</param>
public sealed record JobStatusResult(
    string JobId,
    string JobType,
    JobStatus Status,
    int Progress,
    int Attempts,
    int MaxAttempts,
    bool CancellationRequested,
    string? LastErrorType,
    string? LastErrorMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset NextAttemptUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc) {
    internal static JobStatusResult From(JobRecord record) =>
        new(
            record.JobId,
            record.JobType,
            record.Status,
            record.Progress,
            record.Attempts,
            record.MaxAttempts,
            record.CancellationRequested,
            record.LastErrorType,
            record.LastErrorMessage,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.NextAttemptUtc,
            record.StartedAtUtc,
            record.CompletedAtUtc);
}
