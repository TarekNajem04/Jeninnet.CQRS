//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Shared shutdown steps for transports that gate connect/disconnect with a semaphore and may
/// have a reconnect loop in flight.
/// </summary>
internal static class TransportShutdown {
    private static readonly TimeSpan _reconnectWait = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Waits briefly for an in-flight reconnect loop. A loop blocked in a broker call is
    /// abandoned with the transport lifetime; shutdown must not hang on it.
    /// </summary>
    /// <param name="reconnect">The reconnect task, or <see langword="null"/>.</param>
    public static async Task WaitForReconnectAsync(Task? reconnect) {
        if(reconnect is null) {
            return;
        }

        try {
            await reconnect.WaitAsync(_reconnectWait, CancellationToken.None).ConfigureAwait(false);
        }
        catch {
            // A reconnect blocked in a broker call is abandoned with the lifetime token.
        }
    }

    /// <summary>Disposes the lifetime and releases the connect gate when it was acquired.</summary>
    /// <param name="lifetime">The transport lifetime, or <see langword="null"/>.</param>
    /// <param name="gate">The connect gate.</param>
    /// <param name="gateAcquired">Whether the gate was acquired.</param>
    public static void Complete(CancellationTokenSource? lifetime, SemaphoreSlim gate, bool gateAcquired) {
        lifetime?.Dispose();

        if(gateAcquired) {
            gate.Release();
        }
    }
}