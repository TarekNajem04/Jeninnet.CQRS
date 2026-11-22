//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.DeadLetters;

/// <summary>
/// Durable storage for messages that failed after being accepted: the original envelope, an
/// attempt count, and the last error. Poisons become inspectable data instead of a log line.
/// </summary>
/// <remarks>
/// Replay is a pump operation, not a store operation: the store cannot stage into
/// <see cref="IDeferredStore"/> and wake the pump without depending on the dispatcher.
/// Call <see cref="IDeferredDispatcher.ReplayAsync"/> with an entry's
/// <see cref="DeadLetterEntry.Key"/>.
/// </remarks>
public interface IDeadLetterStore {
    /// <summary>
    /// Records a new failure and returns the assigned key.
    /// </summary>
    /// <param name="envelope">The envelope whose execution failed.</param>
    /// <param name="error">The exception that escaped.</param>
    /// <param name="failedAtUtc">When the failure was observed.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task<string> RecordAsync(IDeferredEnvelope envelope, Exception error, DateTimeOffset failedAtUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Advances an existing entry after a replayed execution failed again: increments the
    /// attempt count and replaces the last error. Unknown keys are ignored.
    /// </summary>
    /// <param name="key">The dead-letter key.</param>
    /// <param name="error">The exception that escaped.</param>
    /// <param name="failedAtUtc">When the failure was observed.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task RecordFailureAsync(string key, Exception error, DateTimeOffset failedAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Lists every entry, most recent failure first.</summary>
    Task<IReadOnlyList<DeadLetterEntry>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets one entry, or <see langword="null"/> when the key is unknown.</summary>
    Task<DeadLetterEntry?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Removes an entry (replayed successfully, or handled manually).</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes entries whose last failure is older than the retention window ending at
    /// <paramref name="now"/>, then trims to <see cref="DeadLetterOptions.MaxEntries"/>.
    /// Returns the number removed.
    /// </summary>
    Task<int> PruneAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}