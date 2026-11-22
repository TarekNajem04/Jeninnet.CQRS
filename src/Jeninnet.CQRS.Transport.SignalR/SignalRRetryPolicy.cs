//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.SignalR;

/// <summary>
/// Reconnect schedule used by <see cref="SignalRTransport"/>. The final configured delay repeats
/// until the connection is restored, so a hub restart longer than the schedule still recovers.
/// </summary>
internal sealed class SignalRRetryPolicy : IRetryPolicy {
    private readonly TimeSpan[] _delays;

    internal SignalRRetryPolicy(TimeSpan[] delays) {
        _delays = delays.Length is 0 ? throw new ArgumentException("At least one reconnect delay is required.", nameof(delays)) : delays;
    }

    /// <inheritdoc />
    public TimeSpan? NextRetryDelay(RetryContext retryContext) {
        var index = Math.Min(retryContext.PreviousRetryCount, _delays.Length - 1);

        return _delays[index];
    }
}