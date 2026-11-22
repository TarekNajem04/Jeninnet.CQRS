//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Serialization;

/// <summary>
/// Serializes durable message payloads. The EF Core stores use this seam instead of calling
/// <c>System.Text.Json</c> directly, so hosts can substitute a compatible format.
/// </summary>
/// <remarks>
/// Implementations must be thread-safe: stores are singletons. A payload written with one
/// serializer must be read with a serializer that reports the same
/// <see cref="ContentType"/>; claim-check reads verify this.
/// </remarks>
public interface IMessageSerializer {
    /// <summary>Gets the content type recorded with claim-checked payloads.</summary>
    string ContentType { get; }

    /// <summary>Serializes <paramref name="message"/> using its runtime <paramref name="messageType"/>.</summary>
    /// <param name="message">The message instance.</param>
    /// <param name="messageType">The runtime type to serialize as.</param>
    byte[] Serialize(object message, Type messageType);

    /// <summary>Deserializes <paramref name="payload"/> as <paramref name="messageType"/>.</summary>
    /// <param name="payload">The serialized bytes.</param>
    /// <param name="messageType">The runtime type to deserialize as.</param>
    object? Deserialize(ReadOnlySpan<byte> payload, Type messageType);
}
