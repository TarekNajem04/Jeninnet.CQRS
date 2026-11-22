//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Receive-side retry policy for one endpoint. Retries happen inside the consumer pipeline before
/// the transport acknowledges or dead-letters the delivery, so a transient handler failure does
/// not become a broker redelivery storm.
/// </summary>
public sealed record EndpointRetryPolicy {
    /// <summary>Gets the policy that never retries (a single attempt).</summary>
    public static EndpointRetryPolicy None { get; } = new() { MaxAttempts = 1 };

    /// <summary>Gets the total number of handler attempts, including the first. Defaults to 1.</summary>
    public int MaxAttempts { get; init; } = 1;

    /// <summary>Gets the delay before the second attempt. Defaults to zero.</summary>
    public TimeSpan Delay { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// Gets the multiplier applied to <see cref="Delay"/> after each failure. Defaults to 1
    /// (constant delay).
    /// </summary>
    public double BackoffMultiplier { get; init; } = 1.0;

    internal void Validate(string endpointName) {
        if(MaxAttempts < 1) {
            throw new EndpointConfigurationException(
                endpointName,
                $"Endpoint '{endpointName}' has an invalid retry policy: max attempts must be at least 1.");
        }

        if(Delay < TimeSpan.Zero || Delay > TimeSpan.FromHours(1)) {
            throw new EndpointConfigurationException(
                endpointName,
                $"Endpoint '{endpointName}' has an invalid retry policy: delay must be between zero and one hour.");
        }

        if(double.IsNaN(BackoffMultiplier) ||
           double.IsInfinity(BackoffMultiplier) ||
           BackoffMultiplier < 1.0 ||
           BackoffMultiplier > 10.0) {
            throw new EndpointConfigurationException(
                endpointName,
                $"Endpoint '{endpointName}' has an invalid retry policy: the backoff multiplier must be between 1 and 10.");
        }
    }

    internal TimeSpan GetDelay(int failedAttempt) {
        if(Delay == TimeSpan.Zero) {
            return TimeSpan.Zero;
        }

        var exponent = Math.Max(0, failedAttempt - 1);
        var milliseconds = Math.Min(
            Delay.TotalMilliseconds * Math.Pow(BackoffMultiplier, exponent),
            TimeSpan.FromHours(1).TotalMilliseconds);

        return TimeSpan.FromMilliseconds(milliseconds);
    }
}