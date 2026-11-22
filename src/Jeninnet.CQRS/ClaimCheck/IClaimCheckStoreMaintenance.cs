//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.ClaimCheck;

/// <summary>
/// Administrative operations for providers that persist claim-check payloads. The three-method
/// <see cref="IClaimCheckStore"/> contract stays minimal; retention is opt-in and provider
/// specific.
/// </summary>
public interface IClaimCheckStoreMaintenance {
    /// <summary>Counts the payloads currently stored, including unreferenced ones.</summary>
    /// <param name="cancellationToken">Cancels the enumeration.</param>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes payloads last written before <paramref name="olderThanUtc"/> that are not in
    /// <paramref name="referencedKeys"/>. Referenced payloads are never removed.
    /// </summary>
    /// <param name="olderThanUtc">The exclusive age cutoff.</param>
    /// <param name="referencedKeys">Keys still referenced by durable rows.</param>
    /// <param name="cancellationToken">Cancels the cleanup.</param>
    /// <returns>The number of deleted payloads.</returns>
    Task<int> CleanupAsync(
        DateTimeOffset olderThanUtc,
        IReadOnlyCollection<string> referencedKeys,
        CancellationToken cancellationToken = default);
}

/// <summary>Time-to-live helpers over <see cref="IClaimCheckStoreMaintenance"/>.</summary>
public static class ClaimCheckMaintenanceExtensions {
    /// <summary>Deletes payloads older than <paramref name="timeToLive"/> that are unreferenced.</summary>
    /// <param name="store">The provider maintenance surface.</param>
    /// <param name="timeToLive">The retention period; zero means everything not referenced.</param>
    /// <param name="referencedKeys">Keys still referenced by durable rows.</param>
    /// <param name="cancellationToken">Cancels the cleanup.</param>
    /// <returns>The number of deleted payloads.</returns>
    public static Task<int> CleanupExpiredAsync(
        this IClaimCheckStoreMaintenance store,
        TimeSpan timeToLive,
        IReadOnlyCollection<string> referencedKeys,
        CancellationToken cancellationToken = default) {
        Guard.NotNull(store);

        if(timeToLive < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(timeToLive), timeToLive, "The time to live must not be negative.");
        }

        var cutoff = TimeProvider.System.GetUtcNow() - timeToLive;

        return store.CleanupAsync(cutoff, referencedKeys, cancellationToken);
    }
}
