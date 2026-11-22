//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency;

/// <summary>
/// Abstraction for storing and retrieving previously handled idempotent command results, and
/// for leasing execution so concurrent callers with the same key run the handler only once.
/// </summary>
/// <remarks>
/// <see cref="TryAcquireAsync{TResponse}"/>, <see cref="RenewAsync"/>,
/// <see cref="CompleteAsync{TResponse}"/>, and <see cref="ReleaseAsync"/> must be atomic with
/// respect to the same key. The in-memory store is atomic in-process; the EF Core store relies
/// on a unique key plus an optimistic-concurrency token, which makes acquisition atomic across
/// processes sharing the database (given the provider's normal row-level guarantees).
/// </remarks>
public interface IIdempotencyStore {
    /// <summary>
    ///   Attempts to get a stored result for the specified key.
    /// </summary>
    /// <typeparam name="TResponse">Response type.</typeparam>
    /// <param name="key">The idempotency key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    ///   The stored response, or <c>default</c> if not present.
    /// </returns>
    Task<TResponse?> GetAsync<TResponse>(
        string key,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///   Stores a result under the specified idempotency key.
    /// </summary>
    /// <typeparam name="TResponse">Response type.</typeparam>
    /// <param name="key">The idempotency key.</param>
    /// <param name="response">The response to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreAsync<TResponse>(
        string key,
        TResponse response,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Atomically tries to lease execution of <paramref name="key"/> for <paramref name="owner"/>.
    /// </summary>
    /// <typeparam name="TResponse">The response type the key is expected to produce.</typeparam>
    /// <param name="key">The idempotency key.</param>
    /// <param name="owner">A unique owner identity for this execution attempt.</param>
    /// <param name="timeToLive">How long the lease stays valid without renewal.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="IdempotencyLeaseState.Acquired"/> when this owner must run the handler,
    /// <see cref="IdempotencyLeaseState.Busy"/> with the current owner when another execution
    /// holds an unexpired lease, or <see cref="IdempotencyLeaseState.Completed"/> with the stored
    /// response. An expired lease is reclaimed by the next caller.
    /// </returns>
    Task<IdempotencyLeaseResult<TResponse>> TryAcquireAsync<TResponse>(
        string key,
        string owner,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extends the lease for <paramref name="owner"/> when it still owns an unexpired lease.
    /// Use this from a long-running handler instead of relying on a very large TTL.
    /// </summary>
    /// <param name="key">The idempotency key.</param>
    /// <param name="owner">The owner recorded by <see cref="TryAcquireAsync{TResponse}"/>.</param>
    /// <param name="timeToLive">The new lifetime measured from now.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the lease was renewed; <see langword="false"/> when it was lost or expired.</returns>
    Task<bool> RenewAsync(
        string key,
        string owner,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the response and releases the lease, but only when <paramref name="owner"/> still
    /// holds it. A stale owner must not overwrite a newer execution's response.
    /// </summary>
    /// <typeparam name="TResponse">Response type.</typeparam>
    /// <param name="key">The idempotency key.</param>
    /// <param name="owner">The owner recorded by <see cref="TryAcquireAsync{TResponse}"/>.</param>
    /// <param name="response">The response to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the response was stored; <see langword="false"/> when the lease was lost.</returns>
    Task<bool> CompleteAsync<TResponse>(
        string key,
        string owner,
        TResponse response,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a lease without storing a response, for example when the handler failed.
    /// </summary>
    /// <param name="key">The idempotency key.</param>
    /// <param name="owner">The owner recorded by <see cref="TryAcquireAsync{TResponse}"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the lease was released; <see langword="false"/> when it was already lost or expired.</returns>
    Task<bool> ReleaseAsync(
        string key,
        string owner,
        CancellationToken cancellationToken = default);
}