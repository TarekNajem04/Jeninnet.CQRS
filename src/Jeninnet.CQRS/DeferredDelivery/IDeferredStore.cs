//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.DeferredDelivery;

/// <summary>
/// Durability log for deferred commands. The pump stages every deferral before
/// queueing it in memory and completes it after execution, so a crash loses
/// nothing that was accepted. Reads and writes are keyed; ordering stays the
/// pump's job (due time, then submission sequence).
/// </summary>
public interface IDeferredStore {
    /// <summary>
    /// Stages <paramref name="envelope"/> durably. Returns the assigned key,
    /// used to <see cref="CompleteAsync"/> the entry after execution.
    /// </summary>
    Task<string> StageAsync(IDeferredEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>Lists every uncompleted entry (pending, due or overdue).</summary>
    Task<IReadOnlyList<(string Key, IDeferredEnvelope Envelope)>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes the entry with <paramref name="key"/> (executed, cancelled, or dropped).</summary>
    Task CompleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Deletes entries expired at <paramref name="now"/>. Returns the number removed.</summary>
    Task<int> PruneExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}