//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Serialization;

/// <summary>
/// Reads inbound transport envelopes with the serializer that matches
/// <see cref="Transports.TransportEnvelope.ContentType"/>. The transport itself stays
/// serialization-agnostic; relay workers use this reader so a new format is configuration, never
/// a branch in dispatch.
/// </summary>
/// <param name="serializers">The content-type registry.</param>
public sealed class TransportMessageReader(IMessageSerializerRegistry serializers) {
    private readonly IMessageSerializerRegistry _serializers = serializers ?? throw new ArgumentNullException(nameof(serializers));

    /// <summary>Gets the registry used by this reader.</summary>
    public IMessageSerializerRegistry Serializers => _serializers;

    /// <summary>
    /// Deserializes <paramref name="envelope"/> with the serializer selected by its content type.
    /// </summary>
    /// <param name="envelope">The delivered envelope.</param>
    /// <param name="messageType">The runtime type to deserialize as.</param>
    /// <exception cref="UnknownContentTypeException">
    /// The envelope declares a content type no serializer understands; dead-letter it.
    /// </exception>
    /// <exception cref="InvalidOperationException">The serializer produced no message.</exception>
    public object Deserialize(TransportEnvelope envelope, Type messageType) {
        Guard.NotNull(envelope);
        Guard.NotNull(messageType);

        return _serializers.Deserialize(envelope.Payload.Span, messageType, envelope.ContentType)
            ?? throw new InvalidOperationException(
                $"The '{envelope.ContentType}' serializer returned no message for '{messageType.FullName}'.");
    }

    /// <summary>
    /// Attempts to deserialize <paramref name="envelope"/> and reports an unknown content type
    /// separately so a consumer can dead-letter it without catching format errors.
    /// </summary>
    /// <param name="envelope">The delivered envelope.</param>
    /// <param name="messageType">The runtime type to deserialize as.</param>
    /// <param name="message">The message when the content type is known.</param>
    /// <param name="error">The unknown-content-type error when negotiation fails.</param>
    public bool TryDeserialize(
        TransportEnvelope envelope,
        Type messageType,
        [NotNullWhen(true)] out object? message,
        [NotNullWhen(false)] out UnknownContentTypeException? error) {
        try {
            message = Deserialize(envelope, messageType);
            error = null;

            return true;
        }
        catch(UnknownContentTypeException exception) {
            message = null;
            error = exception;

            return false;
        }
    }
}
