//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Serialization;

/// <summary>
/// Negotiates wire formats by content type. The default serializer keeps handling outgoing
/// payloads and writes without a content type; registered formats are selected only for
/// payloads whose content type says so.
/// </summary>
/// <remarks>
/// Implementations must be thread-safe: the registry is a singleton. Content types are compared
/// case-insensitively and parameters such as <c>; charset=utf-8</c> are ignored.
/// </remarks>
public interface IMessageSerializerRegistry {
    /// <summary>Gets the serializer used for outgoing payloads and for payloads without a content type.</summary>
    IMessageSerializer Default { get; }

    /// <summary>Gets the registered content types, sorted ordinally.</summary>
    IReadOnlyList<string> ContentTypes { get; }

    /// <summary>
    /// Resolves the serializer for <paramref name="contentType"/>. A null, empty, or whitespace
    /// content type resolves to <see cref="Default"/> so pre-negotiation payloads keep working.
    /// </summary>
    /// <param name="contentType">The wire content type, optionally with parameters.</param>
    /// <exception cref="UnknownContentTypeException">
    /// No serializer is registered for a non-empty content type.
    /// </exception>
    IMessageSerializer Resolve(string? contentType);

    /// <summary>Returns whether a serializer is registered for <paramref name="contentType"/>.</summary>
    /// <param name="contentType">The wire content type, optionally with parameters.</param>
    /// <param name="serializer">The resolved serializer when known; otherwise <see langword="null"/>.</param>
    bool TryResolve(string? contentType, [NotNullWhen(true)] out IMessageSerializer? serializer);

    /// <summary>Deserializes <paramref name="payload"/> using the serializer for <paramref name="contentType"/>.</summary>
    /// <param name="payload">The serialized bytes.</param>
    /// <param name="messageType">The runtime type to deserialize as.</param>
    /// <param name="contentType">The wire content type, optionally with parameters.</param>
    object? Deserialize(ReadOnlySpan<byte> payload, Type messageType, string? contentType);
}
