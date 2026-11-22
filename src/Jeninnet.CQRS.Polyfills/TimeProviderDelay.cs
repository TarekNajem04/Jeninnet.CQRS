//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace System;

/// <summary>Clock-aware delay used by hosted services.</summary>
internal static class TimeProviderDelayExtensions {
    /// <summary>Waits for <paramref name="delay"/> using <paramref name="timeProvider"/>.</summary>
    /// <param name="timeProvider">The clock that owns the timer.</param>
    /// <param name="delay">The delay; non-positive values complete immediately.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    public static Task DelayAsync(this TimeProvider timeProvider, TimeSpan delay, CancellationToken cancellationToken = default) {
#if NETSTANDARD2_0 || NET462
        if(delay == Threading.Timeout.InfiniteTimeSpan) {
            return Task.Delay(Threading.Timeout.Infinite, cancellationToken);
        }

        if(delay <= TimeSpan.Zero) {
            return Task.CompletedTask;
        }

        return DelayWithTimerAsync(timeProvider, delay, cancellationToken);
#else
        return Task.Delay(delay, timeProvider, cancellationToken);
#endif
    }

#if NETSTANDARD2_0 || NET462
    private static async Task DelayWithTimerAsync(TimeProvider timeProvider, TimeSpan delay, CancellationToken cancellationToken) {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timer = timeProvider.CreateTimer(static state => ((TaskCompletionSource<bool>)state).TrySetResult(true), completion, delay, Threading.Timeout.InfiniteTimeSpan);
        using var registration = cancellationToken.Register(state => ((TaskCompletionSource<bool>)state).TrySetCanceled(cancellationToken), completion);

        try {
            await completion.Task.ConfigureAwait(false);
        }
        catch(TaskCanceledException) when(cancellationToken.IsCancellationRequested) {
            throw new OperationCanceledException(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
#endif
}