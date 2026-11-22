//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.RoutingSlips;

/// <summary>
/// Per-step execution and compensation policy. Attempts include the first call; a delay of zero
/// retries immediately. The timeout is applied per attempt, so a slow attempt is canceled before
/// the retry budget is consumed.
/// </summary>
public sealed class RoutingSlipStepOptions {
    /// <summary>Gets or sets the total execution attempts, including the first. Defaults to 1.</summary>
    public int MaxAttempts { get; set; } = 1;

    /// <summary>Gets or sets the delay between execution attempts. Defaults to zero.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.Zero;

    /// <summary>Gets or sets the per-attempt execution timeout. Defaults to no timeout.</summary>
    public TimeSpan Timeout { get; set; } = System.Threading.Timeout.InfiniteTimeSpan;

    /// <summary>Gets or sets the total compensation attempts, including the first. Defaults to 1.</summary>
    public int CompensationMaxAttempts { get; set; } = 1;

    /// <summary>Gets or sets the delay between compensation attempts. Defaults to zero.</summary>
    public TimeSpan CompensationRetryDelay { get; set; } = TimeSpan.Zero;

    /// <summary>Gets or sets the per-attempt compensation timeout. Defaults to no timeout.</summary>
    public TimeSpan CompensationTimeout { get; set; } = System.Threading.Timeout.InfiniteTimeSpan;

    internal void Validate(string stepName) {
        ValidateAttempts(MaxAttempts, stepName, "execution");
        ValidateAttempts(CompensationMaxAttempts, stepName, "compensation");

        if(RetryDelay < TimeSpan.Zero) {
            throw new InvalidOperationException($"Routing-slip step '{stepName}' has a negative retry delay.");
        }

        if(CompensationRetryDelay < TimeSpan.Zero) {
            throw new InvalidOperationException($"Routing-slip step '{stepName}' has a negative compensation retry delay.");
        }

        ValidateTimeout(Timeout, stepName, "execution");
        ValidateTimeout(CompensationTimeout, stepName, "compensation");
    }

    internal RoutingSlipStepOptions Copy() =>
        new() {
            MaxAttempts = MaxAttempts,
            RetryDelay = RetryDelay,
            Timeout = Timeout,
            CompensationMaxAttempts = CompensationMaxAttempts,
            CompensationRetryDelay = CompensationRetryDelay,
            CompensationTimeout = CompensationTimeout,
        };

    private static void ValidateAttempts(int attempts, string stepName, string kind) {
        if(attempts < 1) {
            throw new InvalidOperationException($"Routing-slip step '{stepName}' {kind} attempts must be at least 1.");
        }
    }

    private static void ValidateTimeout(TimeSpan timeout, string stepName, string kind) {
        if(timeout != System.Threading.Timeout.InfiniteTimeSpan && timeout <= TimeSpan.Zero) {
            throw new InvalidOperationException(
                $"Routing-slip step '{stepName}' {kind} timeout must be Timeout.InfiniteTimeSpan or a positive duration.");
        }
    }
}