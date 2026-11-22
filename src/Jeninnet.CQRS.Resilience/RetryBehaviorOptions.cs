//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Resilience;

/// <summary>
/// Options controlling the retry behavior: number of attempts and base delay for exponential back-off.
/// </summary>
public sealed class RetryBehaviorOptions : IOptions<RetryBehaviorOptions> {
    /// <summary>Gets the default configuration section name.</summary>
    public static string SectionName { get; internal set; } = "RetryBehaviorOptions";

    /// <summary>
    ///   Maximum number of retries on exception.
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    ///   Base delay in milliseconds; each retry waits
    ///   <c>BaseDelay * 2^(attempt-1)</c>.
    /// </summary>
    public int BaseDelayMilliseconds { get; set; } = 200;

    /// <summary>
    /// Enables a simple circuit breaker that opens after repeated failures.
    /// </summary>
    public bool EnableCircuitBreaker { get; set; }

    /// <summary>
    /// Timeout for each handler execution.
    /// </summary>
    public int TimeoutSeconds { get; set; } // 0 = disabled

    /// <summary>
    /// Number of consecutive failures before the circuit opens.
    /// </summary>
    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    /// <summary>
    /// How long the circuit stays open.
    /// </summary>
    public int CircuitBreakerOpenSeconds { get; set; } = 30;

    /// <summary>
    /// Gets the current retry behavior options instance.
    /// </summary>
    public RetryBehaviorOptions Value => this;
}