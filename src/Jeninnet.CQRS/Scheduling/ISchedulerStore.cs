//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Scheduling;

/// <summary>
/// Persistent scheduler store. Rows are leased with a version guard, so two workers cannot both
/// fire the same due row; a one-shot row is deleted after success and a recurring row is moved to
/// its next occurrence.
/// </summary>
public interface ISchedulerStore {
    /// <summary>Adds a schedule; returns false when the key already exists.</summary>
    /// <param name="message">The schedule row.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task<bool> TryAddAsync(ScheduledMessage message, CancellationToken cancellationToken = default);

    /// <summary>Leases due rows for <paramref name="owner"/>.</summary>
    /// <param name="owner">The worker owner id.</param>
    /// <param name="batchSize">Maximum rows to lease.</param>
    /// <param name="now">The current UTC time.</param>
    /// <param name="leaseDuration">How long the claim lasts.</param>
    /// <param name="cancellationToken">Cancels the read/claim.</param>
    Task<IReadOnlyList<SchedulerLease>> LeaseDueAsync(string owner, int batchSize, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken = default);

    /// <summary>Removes a one-shot row after it fires; false when the lease/version was lost.</summary>
    /// <param name="key">The schedule key.</param>
    /// <param name="owner">The expected lease owner.</param>
    /// <param name="version">The leased version.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    Task<bool> CompleteAsync(string key, string owner, long version, CancellationToken cancellationToken = default);

    /// <summary>Moves a recurring row to its next occurrence; false when the lease/version was lost.</summary>
    /// <param name="key">The schedule key.</param>
    /// <param name="owner">The expected lease owner.</param>
    /// <param name="version">The leased version.</param>
    /// <param name="nextRunUtc">The next due instant.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    Task<bool> ScheduleNextAsync(string key, string owner, long version, DateTimeOffset nextRunUtc, CancellationToken cancellationToken = default);

    /// <summary>Records a failed publish and schedules a retry.</summary>
    /// <param name="key">The schedule key.</param>
    /// <param name="owner">The expected lease owner.</param>
    /// <param name="version">The leased version.</param>
    /// <param name="error">The failure.</param>
    /// <param name="attempts">The attempt count after this failure.</param>
    /// <param name="nextRunUtc">The retry instant.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    Task<bool> FailAsync(string key, string owner, long version, Exception error, int attempts, DateTimeOffset nextRunUtc, CancellationToken cancellationToken = default);

    /// <summary>Removes a schedule. A leased row is still processed by its owner.</summary>
    /// <param name="key">The schedule key.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    Task<bool> CancelAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Lists schedules, ordered by next run.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<ScheduledMessage>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Counts rows due at or before <paramref name="now"/>.</summary>
    /// <param name="now">The current UTC time.</param>
    /// <param name="cancellationToken">Cancels the count.</param>
    Task<long> CountDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
