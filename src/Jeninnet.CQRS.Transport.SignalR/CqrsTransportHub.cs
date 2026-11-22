//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
#if !NETSTANDARD2_0
using Microsoft.AspNetCore.SignalR;

namespace Jeninnet.CQRS.Transport.SignalR;

/// <summary>
/// Minimal relay hub for <see cref="SignalRTransport"/>. It owns no queue and no state: a
/// <c>Send</c> returns the envelope to the calling connection, while <c>Publish</c> fans it out to
/// every connected client. For server-initiated messages, resolve
/// <see cref="IHubContext{THub}"/> and call the delivery method directly.
/// </summary>
/// <remarks>
/// SignalR delivery is ephemeral and at-most-once across live connections; a client that is
/// disconnected misses messages. Use an outbox/relay for durable handoff and configure a backplane
/// (for example Redis) when more than one server hosts the hub.
/// </remarks>
public sealed class CqrsTransportHub : Hub {
    /// <summary>Returns an envelope to the calling connection.</summary>
    /// <param name="envelope">The serialized envelope.</param>
    /// <param name="cancellationToken">Cancels the delivery.</param>
    public Task Send(SignalRWireEnvelope envelope, CancellationToken cancellationToken = default) {
        Guard.NotNull(envelope);

        return Clients.Caller.SendAsync(SignalRTransportProtocol.DeliveryMethod, envelope, cancellationToken);
    }

    /// <summary>Fans an envelope out to every connected client, including the caller.</summary>
    /// <param name="envelope">The serialized envelope.</param>
    /// <param name="cancellationToken">Cancels the delivery.</param>
    public Task Publish(SignalRWireEnvelope envelope, CancellationToken cancellationToken = default) {
        Guard.NotNull(envelope);

        return Clients.All.SendAsync(SignalRTransportProtocol.DeliveryMethod, envelope, cancellationToken);
    }
}
#endif