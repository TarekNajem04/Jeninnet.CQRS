//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Outbox;

/// <summary>
/// Durable staging for notifications published inside a transaction. The base contract is
/// stage → list pending → complete; the lease methods add the relay semantics used by
/// <see cref="OutboxRelayWorker{TTransport}"/>: one owner per row, persisted attempts, and
/// exponential backoff after a failed publish.
/// </summary>
public interface IOutboxStore {
    /// <summary>
    /// Stages a notification durably and returns the assigned key.
    /// </summary>
    /// <param name="notification">The notification envelope.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task<string> StageAsync(IDeferredEnvelope notification, CancellationToken cancellationToken = default);

    /// <summary>Lists every notification that has not completed yet.</summary>
    Task<IReadOnlyList<(string Key, IDeferredEnvelope Notification)>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes a delivered notification.</summary>
    Task CompleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically leases up to <paramref name="batchSize"/> due rows to <paramref name="owner"/>.
    /// Rows already leased by another owner, or waiting out a retry backoff, are skipped.
    /// </summary>
    /// <param name="owner">The unique worker owner id.</param>
    /// <param name="batchSize">Maximum rows to claim.</param>
    /// <param name="leaseDuration">How long the claim lasts.</param>
    /// <param name="now">The current UTC time, supplied by the caller's clock.</param>
    /// <param name="cancellationToken">Cancels the read and claim.</param>
    Task<IReadOnlyList<OutboxLease>> LeaseAsync(
        string owner,
        int batchSize,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a row only when <paramref name="owner"/> still holds an unexpired lease.
    /// Returns whether the row was removed.
    /// </summary>
    /// <param name="key">The row key.</param>
    /// <param name="owner">The expected lease owner.</param>
    /// <param name="now">The current UTC time.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    Task<bool> CompleteLeaseAsync(string key, string owner, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a failed publish, schedules the next attempt, and releases the lease. Returns
    /// whether the row was still owned by <paramref name="owner"/>.
    /// </summary>
    /// <param name="key">The row key.</param>
    /// <param name="owner">The expected lease owner.</param>
    /// <param name="error">The publish failure.</param>
    /// <param name="attempts">The attempt count after this failure.</param>
    /// <param name="nextAttemptUtc">When the row becomes eligible again.</param>
    /// <param name="now">The current UTC time.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    Task<bool> FailLeaseAsync(
        string key,
        string owner,
        Exception error,
        int attempts,
        DateTimeOffset nextAttemptUtc,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases an unprocessed lease so another worker can claim it immediately. Returns whether
    /// the row was still owned by <paramref name="owner"/>.
    /// </summary>
    /// <param name="key">The row key.</param>
    /// <param name="owner">The expected lease owner.</param>
    /// <param name="now">The current UTC time.</param>
    /// <param name="cancellationToken">Cancels the release.</param>
    Task<bool> ReleaseLeaseAsync(string key, string owner, DateTimeOffset now, CancellationToken cancellationToken = default);
}