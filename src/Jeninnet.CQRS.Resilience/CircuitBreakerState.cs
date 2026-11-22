//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Resilience;

/// <summary>
/// The states of the <see cref="RetryBehavior{TRequest, TResponse}"/> circuit breaker.
/// </summary>
public enum CircuitBreakerState {
    /// <summary>Requests flow normally; failures are counted.</summary>
    Closed = 0,

    /// <summary>Requests fail fast without invoking the handler.</summary>
    Open = 1,

    /// <summary>Exactly one trial request probes the downstream; the rest wait for its outcome.</summary>
    HalfOpen = 2
}

/// <summary>
/// A point-in-time view of the circuit breaker, for health checks and diagnostics.
/// </summary>
/// <param name="State">The current breaker state.</param>
/// <param name="ConsecutiveFailures">The current consecutive-failure count.</param>
/// <param name="OpenedAtUtc">When the breaker last opened, or <see langword="null"/> when closed.</param>
public readonly record struct CircuitBreakerSnapshot(
    CircuitBreakerState State,
    int ConsecutiveFailures,
    DateTime? OpenedAtUtc
);

/// <summary>
/// Notifies when the circuit breaker changes state.
/// </summary>
/// <param name="OldState">The state before the transition.</param>
/// <param name="NewState">The state after the transition.</param>
public sealed class CircuitBreakerStateChangedEventArgs(CircuitBreakerState OldState, CircuitBreakerState NewState) : EventArgs {
    /// <summary>Gets the state before the transition.</summary>
    public CircuitBreakerState OldState { get; } = OldState;

    /// <summary>Gets the state after the transition.</summary>
    public CircuitBreakerState NewState { get; } = NewState;
}