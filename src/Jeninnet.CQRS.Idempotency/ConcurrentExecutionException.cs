//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency;

/// <summary>
/// Thrown when an idempotent command cannot obtain its execution lease: another owner is still
/// executing (fail-fast mode) or the bounded wait elapsed without a stored response.
/// </summary>
public sealed class ConcurrentExecutionException : Exception {
    /// <summary>Initializes the exception with a descriptive message.</summary>
    /// <param name="idempotencyKey">The key whose lease is busy.</param>
    /// <param name="owner">The current owner, when known.</param>
    /// <param name="message">The failure message.</param>
    /// <param name="innerException">The underlying failure, if any.</param>
    public ConcurrentExecutionException(string idempotencyKey, string? owner, string message, Exception? innerException = null)
        : base(message, innerException) {
        IdempotencyKey = idempotencyKey ?? throw new ArgumentNullException(nameof(idempotencyKey));
        Owner = owner;
    }

    /// <summary>Gets the idempotency key whose execution lease could not be obtained.</summary>
    public string IdempotencyKey { get; }

    /// <summary>Gets the owner observed while the lease was busy, when known.</summary>
    public string? Owner { get; }
}
