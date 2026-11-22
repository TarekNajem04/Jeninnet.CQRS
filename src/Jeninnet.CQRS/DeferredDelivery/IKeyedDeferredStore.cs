//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.DeferredDelivery;

/// <summary>
/// A deferred store that can stage an envelope under a caller-supplied key, returning false when
/// the key already exists. Recurring schedules use this for deterministic, deduplicated
/// occurrence keys.
/// </summary>
public interface IKeyedDeferredStore : IDeferredStore {
    /// <summary>
    /// Inserts <paramref name="envelope"/> under <paramref name="key"/> when absent.
    /// </summary>
    /// <param name="key">The deterministic key.</param>
    /// <param name="envelope">The envelope to stage.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when inserted; <see langword="false"/> when the key exists.</returns>
    Task<bool> TryStageAsync(string key, IDeferredEnvelope envelope, CancellationToken cancellationToken = default);
}
