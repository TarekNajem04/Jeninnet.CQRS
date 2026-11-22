//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Transport-agnostic message publishing. Send is point-to-point (one consumer); publish fans out
/// to every subscriber. The transport owns serialization/envelope construction and lifecycle;
/// consumer dispatch stays outside the transport (an <see cref="IMessageTransportHandler"/> or a
/// relay worker owns it).
/// </summary>
public interface IMessageTransport {
    /// <summary>Sends an already-typed message (used by relay workers holding an envelope).</summary>
    /// <param name="message">The message instance.</param>
    /// <param name="messageType">The runtime message type.</param>
    /// <param name="options">Optional envelope metadata.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    Task SendAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Publishes an already-typed message (used by relay workers holding an envelope).</summary>
    /// <param name="message">The message instance.</param>
    /// <param name="messageType">The runtime message type.</param>
    /// <param name="options">Optional envelope metadata.</param>
    /// <param name="cancellationToken">Cancels the publish.</param>
    Task PublishAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Starts accepting messages. Idempotent.</summary>
    /// <param name="cancellationToken">Cancels startup.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops accepting messages. Idempotent; later sends/publishes must fail.</summary>
    /// <param name="cancellationToken">Cancels shutdown.</param>
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>Typed convenience overloads for <see cref="IMessageTransport"/>.</summary>
public static class MessageTransportExtensions {
    /// <summary>Sends <paramref name="message"/> to one consumer of the message type.</summary>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <param name="transport">The transport.</param>
    /// <param name="message">The message instance.</param>
    /// <param name="options">Optional envelope metadata (correlation, causation, tenant, headers).</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    public static Task SendAsync<TMessage>(this IMessageTransport transport, TMessage message, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) {
        Guard.NotNull(transport);

        return transport.SendAsync(message!, typeof(TMessage), options, cancellationToken);
    }

    /// <summary>Publishes <paramref name="message"/> to every subscriber of the message type.</summary>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <param name="transport">The transport.</param>
    /// <param name="message">The message instance.</param>
    /// <param name="options">Optional envelope metadata (correlation, causation, tenant, headers).</param>
    /// <param name="cancellationToken">Cancels the publish.</param>
    public static Task PublishAsync<TMessage>(this IMessageTransport transport, TMessage message, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) {
        Guard.NotNull(transport);

        return transport.PublishAsync(message!, typeof(TMessage), options, cancellationToken);
    }
}