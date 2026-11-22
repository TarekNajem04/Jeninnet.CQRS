//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Jobs;

/// <summary>
/// Job persistence seam. Rows are leased with a version guard; a queued row becomes due at
/// <see cref="JobRecord.NextAttemptUtc"/>, and an expired running lease is reclaimable by another
/// worker. Terminal rows are immutable.
/// </summary>
public interface IJobStore {
    /// <summary>Inserts a queued job and returns it at version 1.</summary>
    /// <param name="record">The new job row; its version is ignored.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task<JobRecord> CreateAsync(JobRecord record, CancellationToken cancellationToken = default);

    /// <summary>Loads one job, or returns <see langword="null"/> when the id is unknown.</summary>
    /// <param name="jobId">The job id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<JobRecord?> GetAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Leases due jobs for <paramref name="owner"/>: queued rows at or before
    /// <paramref name="now"/>, plus running rows whose lease expired. Each leased row increments
    /// <see cref="JobRecord.Attempts"/> and records the owner/lease.
    /// </summary>
    /// <param name="owner">The worker owner id.</param>
    /// <param name="batchSize">Maximum rows to lease.</param>
    /// <param name="now">The current UTC time.</param>
    /// <param name="leaseDuration">How long the claim lasts.</param>
    /// <param name="cancellationToken">Cancels the claim.</param>
    Task<IReadOnlyList<JobRecord>> LeaseDueAsync(
        string owner,
        int batchSize,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    /// <summary>Reports progress for a running job owned by <paramref name="owner"/>.</summary>
    /// <param name="jobId">The job id.</param>
    /// <param name="owner">The worker lease owner.</param>
    /// <param name="progress">Progress in the 0–100 range.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="false"/> when the lease was lost or the job is no longer running.</returns>
    Task<bool> ReportProgressAsync(string jobId, string owner, int progress, CancellationToken cancellationToken = default);

    /// <summary>Extends a running lease owned by <paramref name="owner"/>.</summary>
    /// <param name="jobId">The job id.</param>
    /// <param name="owner">The worker lease owner.</param>
    /// <param name="leaseDuration">The new lease duration from now.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task<bool> RenewLeaseAsync(string jobId, string owner, TimeSpan leaseDuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests cancellation. A queued job becomes <see cref="JobStatus.Canceled"/> immediately; a
    /// running job gets the flag and the worker stops it cooperatively. Terminal ids are a no-op.
    /// </summary>
    /// <param name="jobId">The job id.</param>
    /// <param name="now">The current UTC time.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when the job exists (including already-terminal jobs).</returns>
    Task<bool> RequestCancellationAsync(string jobId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Reads the cancellation flag for the worker watcher.</summary>
    /// <param name="jobId">The job id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<bool> IsCancellationRequestedAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>Writes a terminal status for a running job owned by <paramref name="owner"/>.</summary>
    /// <param name="jobId">The job id.</param>
    /// <param name="owner">The worker lease owner.</param>
    /// <param name="status">A terminal status.</param>
    /// <param name="progress">The final progress, 0–100.</param>
    /// <param name="error">The terminal failure, when any.</param>
    /// <param name="now">The current UTC time.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="false"/> when the lease was lost or the row is already terminal.</returns>
    Task<bool> CompleteAsync(
        string jobId,
        string owner,
        JobStatus status,
        int progress,
        Exception? error,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a failed attempt to the queue with its next due time.</summary>
    /// <param name="jobId">The job id.</param>
    /// <param name="owner">The worker lease owner.</param>
    /// <param name="error">The failure.</param>
    /// <param name="nextAttemptUtc">When the next attempt is due.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task<bool> RetryAsync(
        string jobId,
        string owner,
        Exception error,
        DateTimeOffset nextAttemptUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes terminal jobs completed at or before the cutoff.</summary>
    /// <param name="completedBeforeUtc">The retention cutoff.</param>
    /// <param name="cancellationToken">Cancels the cleanup.</param>
    /// <returns>The number of rows removed.</returns>
    Task<int> PruneAsync(DateTimeOffset completedBeforeUtc, CancellationToken cancellationToken = default);

    /// <summary>Counts jobs with one status; used by the active gauge.</summary>
    /// <param name="status">The status to count.</param>
    /// <param name="cancellationToken">Cancels the count.</param>
    Task<long> CountAsync(JobStatus status, CancellationToken cancellationToken = default);
}
