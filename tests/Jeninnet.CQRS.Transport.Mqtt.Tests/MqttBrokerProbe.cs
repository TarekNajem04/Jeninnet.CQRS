//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Mqtt.Tests;

/// <summary>
/// Waits until an MQTT broker accepts TCP connections. Testcontainers reports a container as
/// started before Mosquitto has bound its listener, and an in-process MQTTnet server can return
/// from <c>StartAsync</c> before the socket is reachable. Both cases otherwise surface as a
/// connection-refused race in the first test.
/// </summary>
internal static class MqttBrokerProbe {

    /// <summary>
    /// Reserves an ephemeral loopback port and releases it so a container can bind the same port.
    /// </summary>
    /// <returns>A TCP port that was free at the time of the call.</returns>
    internal static int GetFreePort() {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally {
            listener.Stop();
        }
    }

    /// <summary>
    /// Probes <paramref name="host"/>:<paramref name="port"/> until a TCP connection succeeds.
    /// </summary>
    /// <param name="host">Broker host name.</param>
    /// <param name="port">Broker port.</param>
    /// <param name="cancellationToken">Cancellation token for the probe.</param>
    /// <param name="timeoutMilliseconds">Maximum time to wait, in milliseconds.</param>
    /// <returns>A task that completes when the broker accepts a connection.</returns>
    /// <exception cref="InvalidOperationException">The broker did not accept a connection in time.</exception>
    internal static async Task WaitForPortAsync(
        string host,
        int port,
        CancellationToken cancellationToken,
        int timeoutMilliseconds = 30_000) {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        Exception? lastFailure = null;

        while(Environment.TickCount64 < deadline) {
            cancellationToken.ThrowIfCancellationRequested();

            try {
                using var client = new TcpClient();
                await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);

                return;
            }
            catch(Exception exception) when(exception is SocketException or IOException) {
                lastFailure = exception;
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException(
            $"MQTT broker at {host}:{port} did not accept connections within {timeoutMilliseconds} ms.",
            lastFailure);
    }
}