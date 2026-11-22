//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.SignalR;

/// <summary>
/// Configuration for the SignalR client transport. The hub path and the connection options are
/// application configuration; authentication tokens are supplied through
/// <see cref="ConfigureConnection"/>.
/// </summary>
public sealed class SignalRTransportOptions {
    /// <summary>Gets or sets the hub URL, absolute or relative to the host's base address.</summary>
    public string HubUrl { get; set; } = "/cqrs";

    /// <summary>
    /// Gets or sets whether the client reconnects automatically after an unexpected disconnect.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool AutomaticReconnect { get; set; } = true;

    /// <summary>
    /// Gets or sets the reconnect delay schedule. The final entry repeats until the connection is
    /// restored. Defaults to immediate, then 2, 5, 10, and 30 seconds.
    /// </summary>
    public IList<TimeSpan> ReconnectDelays { get; } = [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
    ];

    /// <summary>Gets or sets the initial connection timeout. Defaults to 30 seconds.</summary>
    public TimeSpan StartTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets an optional callback that customizes the underlying HTTP connection, for
    /// example to add an access-token provider or a test-server message handler.
    /// </summary>
    public Action<HttpConnectionOptions>? ConfigureConnection { get; set; }
}
