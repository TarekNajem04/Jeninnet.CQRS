//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Observers;

/// <summary>
/// Read-only view of one outbound message presented to send and publish observers. The envelope is
/// the canonical broker-neutral record; observers must not mutate the message or its headers.
/// </summary>
public sealed class OutboundObservationContext {
    /// <summary>Initializes an outbound observation context.</summary>
    /// <param name="message">The typed message instance.</param>
    /// <param name="messageType">The runtime message type.</param>
    /// <param name="envelope">The canonical envelope the transport is about to send.</param>
    public OutboundObservationContext(object message, Type messageType, TransportEnvelope envelope) {
        Message = message ?? throw new ArgumentNullException(nameof(message));
        MessageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
        Envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
    }

    /// <summary>Gets the typed message instance.</summary>
    public object Message { get; }

    /// <summary>Gets the runtime message type.</summary>
    public Type MessageType { get; }

    /// <summary>Gets the canonical envelope.</summary>
    public TransportEnvelope Envelope { get; }
}

/// <summary>
/// Read-only view of one inbound delivery presented to consume observers. The endpoint may be
/// <see langword="null"/> when the message type has no explicit configuration.
/// </summary>
public sealed class ConsumeObservationContext {
    /// <summary>Initializes a consume observation context.</summary>
    /// <param name="envelope">The delivered envelope.</param>
    /// <param name="endpoint">The endpoint policy, or <see langword="null"/>.</param>
    public ConsumeObservationContext(TransportEnvelope envelope, EndpointConfiguration? endpoint) {
        Envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
        Endpoint = endpoint;
    }

    /// <summary>Gets the delivered envelope.</summary>
    public TransportEnvelope Envelope { get; }

    /// <summary>Gets the endpoint policy, or <see langword="null"/> for an unconfigured type.</summary>
    public EndpointConfiguration? Endpoint { get; }
}
