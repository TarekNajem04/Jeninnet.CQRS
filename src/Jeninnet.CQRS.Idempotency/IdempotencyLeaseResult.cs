//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency;

/// <summary>The state observed by <see cref="IIdempotencyStore.TryAcquireAsync{TResponse}"/>.</summary>
public enum IdempotencyLeaseState {
    /// <summary>This caller owns the execution lease and must run the handler.</summary>
    Acquired = 0,

    /// <summary>Another owner holds an unexpired lease.</summary>
    Busy = 1,

    /// <summary>A completed response is already stored for the key.</summary>
    Completed = 2,
}

/// <summary>
/// The outcome of an atomic idempotency-lease acquisition: acquired by this caller, busy with
/// another owner, or already completed with the stored response.
/// </summary>
/// <typeparam name="TResponse">The response type associated with the key.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Static factories are the intended API shape for this generic result type.")]
public readonly struct IdempotencyLeaseResult<TResponse> {
    private IdempotencyLeaseResult(IdempotencyLeaseState state, string? owner, DateTimeOffset? expiresAtUtc, TResponse? response) {
        State = state;
        Owner = owner;
        ExpiresAtUtc = expiresAtUtc;
        Response = response;
    }

    /// <summary>Gets the observed lease state.</summary>
    public IdempotencyLeaseState State { get; }

    /// <summary>Gets the current or previous owner when <see cref="State"/> is Acquired or Busy.</summary>
    public string? Owner { get; }

    /// <summary>Gets when the lease expires, when <see cref="State"/> is Acquired or Busy.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; }

    /// <summary>Gets the stored response when <see cref="State"/> is Completed.</summary>
    public TResponse? Response { get; }

    /// <summary>Creates an acquired result.</summary>
    /// <param name="owner">The owner that now holds the lease.</param>
    /// <param name="expiresAtUtc">When the lease expires unless renewed.</param>
    public static IdempotencyLeaseResult<TResponse> Acquired(string owner, DateTimeOffset expiresAtUtc) =>
        new(IdempotencyLeaseState.Acquired, owner ?? throw new ArgumentNullException(nameof(owner)), expiresAtUtc, default);

    /// <summary>Creates a busy result.</summary>
    /// <param name="owner">The owner currently holding the lease.</param>
    /// <param name="expiresAtUtc">When the current lease expires unless renewed.</param>
    public static IdempotencyLeaseResult<TResponse> Busy(string owner, DateTimeOffset expiresAtUtc) =>
        new(IdempotencyLeaseState.Busy, owner ?? throw new ArgumentNullException(nameof(owner)), expiresAtUtc, default);

    /// <summary>Creates a completed result carrying the stored response.</summary>
    /// <param name="response">The stored response; may be <see langword="null"/> when the command returned null.</param>
    public static IdempotencyLeaseResult<TResponse> Completed(TResponse? response) =>
        new(IdempotencyLeaseState.Completed, null, null, response);
}
