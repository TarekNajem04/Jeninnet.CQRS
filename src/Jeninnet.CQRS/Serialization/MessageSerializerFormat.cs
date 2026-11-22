//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Serialization;

/// <summary>
/// Registers an additional wire format with <see cref="MessageSerializerRegistry"/> without
/// replacing the default <see cref="IMessageSerializer"/> used by durable stores and transports.
/// Optional format packages register one of these; consumers negotiate by content type.
/// </summary>
/// <param name="serializer">The format serializer. Never <see langword="null"/>.</param>
public sealed class MessageSerializerFormat(IMessageSerializer serializer) {
    /// <summary>Gets the format serializer.</summary>
    public IMessageSerializer Serializer { get; } = serializer ?? throw new ArgumentNullException(nameof(serializer));

    /// <summary>Gets the content type declared by the format serializer.</summary>
    public string ContentType => Serializer.ContentType;
}
