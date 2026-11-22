//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Everything a receive filter sees for one delivery: the decoded message, the canonical envelope
/// (including headers), and the endpoint policy that selected the filter.
/// </summary>
public sealed class ReceiveContext {
    /// <summary>Initializes a receive context.</summary>
    /// <param name="message">The deserialized message.</param>
    /// <param name="envelope">The delivered envelope.</param>
    /// <param name="endpoint">The endpoint policy, or the default policy for an unconfigured type.</param>
    public ReceiveContext(object message, TransportEnvelope envelope, EndpointConfiguration endpoint) {
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
    }

    /// <summary>Gets the deserialized message.</summary>
    public object Message { get; }

    /// <summary>Gets the delivered envelope, including transport-neutral headers.</summary>
    public TransportEnvelope Envelope { get; }

    /// <summary>Gets the endpoint policy that owns this delivery.</summary>
    public EndpointConfiguration Endpoint { get; }

    /// <summary>Gets the envelope headers; shorthand for <c>Envelope.Headers</c>.</summary>
    public IReadOnlyDictionary<string, string> Headers => Envelope.Headers;

    /// <summary>Casts <see cref="Message"/> to the filter's message type.</summary>
    /// <typeparam name="TMessage">The expected message type.</typeparam>
    /// <returns>The decoded message.</returns>
    public TMessage MessageAs<TMessage>() => (TMessage)Message;
}
