//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// The transport-neutral input to consumer middleware: the delivered envelope and the endpoint
/// policy that owns it. The payload stays encoded so middleware that only needs metadata (for
/// example the inbox) does not force deserialization.
/// </summary>
public sealed class ReceiveMiddlewareContext {
    /// <summary>Initializes a middleware context.</summary>
    /// <param name="envelope">The delivered envelope.</param>
    /// <param name="endpoint">The endpoint policy, or <see langword="null"/> for an unconfigured message type.</param>
    public ReceiveMiddlewareContext(TransportEnvelope envelope, EndpointConfiguration? endpoint) {
        Envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
        Endpoint = endpoint;
    }

    /// <summary>Gets the delivered envelope.</summary>
    public TransportEnvelope Envelope { get; }

    /// <summary>Gets the endpoint policy, or <see langword="null"/> when the type uses transport defaults.</summary>
    public EndpointConfiguration? Endpoint { get; }

    /// <summary>Gets the envelope headers; shorthand for <c>Envelope.Headers</c>.</summary>
    public IReadOnlyDictionary<string, string> Headers => Envelope.Headers;
}