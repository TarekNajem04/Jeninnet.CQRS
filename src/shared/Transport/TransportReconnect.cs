//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Shared reconnect scheduling for transports whose broker client can report a dropped
/// connection. The transport supplies the broker-specific connect call; the backoff,
/// cancellation, single-flight guard, and completion reset are identical everywhere.
/// </summary>
internal static class TransportReconnect {
    /// <summary>
    /// Starts one reconnect loop unless a loop is already scheduled or the transport is stopping.
    /// </summary>
    /// <param name="scheduled">The transport's single-flight flag.</param>
    /// <param name="stopping">Whether the transport is stopping.</param>
    /// <param name="lifetime">The transport lifetime, or <see langword="null"/> when not started.</param>
    /// <param name="loop">The broker-specific reconnect loop.</param>
    /// <param name="task">The running loop task when one was started.</param>
    /// <returns><see langword="true"/> when a loop was started.</returns>
    public static bool TryStart(
        ref int scheduled,
        bool stopping,
        CancellationTokenSource? lifetime,
        Func<CancellationToken, Task> loop,
        out Task? task) {
        task = null;

        if(stopping || Interlocked.CompareExchange(ref scheduled, 1, 0) is not 0) {
            return false;
        }

        if(lifetime is null) {
            Interlocked.Exchange(ref scheduled, 0);

            return false;
        }

        task = Task.Run(() => loop(lifetime.Token), CancellationToken.None);

        return true;
    }

    /// <summary>
    /// Runs a reconnect loop with capped exponential backoff until the broker accepts one connect
    /// or the lifetime is canceled.
    /// </summary>
    /// <param name="connect">The broker-specific connect operation.</param>
    /// <param name="initialDelay">The first retry delay.</param>
    /// <param name="maxDelay">The backoff cap.</param>
    /// <param name="onFailure">Optional failure observer.</param>
    /// <param name="onCompleted">Resets the transport's single-flight flag.</param>
    /// <param name="cancellationToken">The transport lifetime token.</param>
    public static async Task RunAsync(
        Func<CancellationToken, Task> connect,
        TimeSpan initialDelay,
        TimeSpan maxDelay,
        Action<Exception>? onFailure,
        Action onCompleted,
        CancellationToken cancellationToken) {
        var delay = initialDelay;

        try {
            while(!cancellationToken.IsCancellationRequested) {
                try {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch(OperationCanceledException) {
                    return;
                }

                try {
                    await connect(cancellationToken).ConfigureAwait(false);

                    return;
                }
                catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
                    return;
                }
                catch(Exception error) {
                    onFailure?.Invoke(error);

                    var next = delay.Ticks * 2;
                    delay = TimeSpan.FromTicks(Math.Min(next, maxDelay.Ticks));
                }
            }
        }
        finally {
            onCompleted();
        }
    }
}
