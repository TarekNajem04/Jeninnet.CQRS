//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Serialization.MessagePack;

/// <summary>
/// MessagePack <see cref="IMessageSerializer"/> for compact binary payloads on
/// <c>application/x-msgpack</c>. The default options use MessagePack's standard resolver, which
/// discovers source-generated formatters for <c>[MessagePackObject]</c> types and is AOT-safe.
/// </summary>
/// <param name="options">The MessagePack options. Never <see langword="null"/>.</param>
public sealed class MessagePackMessageSerializer(IOptions<MessagePackMessageSerializerOptions> options) : IMessageSerializer {
    /// <summary>The content type this serializer declares and reads.</summary>
    public const string ContentTypeName = "application/x-msgpack";

    private readonly MessagePackSerializerOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value.Serializer;

    /// <inheritdoc />
    public string ContentType => ContentTypeName;

    /// <inheritdoc />
    public byte[] Serialize(object message, Type messageType) {
        Guard.NotNull(message);
        Guard.NotNull(messageType);

        return MessagePackSerializer.Serialize(messageType, message, _options);
    }

    /// <inheritdoc />
    public object? Deserialize(ReadOnlySpan<byte> payload, Type messageType) {
        Guard.NotNull(messageType);

        return MessagePackSerializer.Deserialize(messageType, payload.ToArray(), _options);
    }
}
