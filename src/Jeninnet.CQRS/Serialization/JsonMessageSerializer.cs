//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Serialization;

/// <summary>
/// Default <see cref="IMessageSerializer"/>: UTF-8 JSON with web defaults, matching the payloads
/// the stores wrote before the seam existed.
/// </summary>
/// <param name="options">The JSON options. Never <see langword="null"/>.</param>
public sealed class JsonMessageSerializer(IOptions<JsonMessageSerializerOptions> options) : IMessageSerializer {
    /// <summary>The content type this serializer declares and reads.</summary>
    public const string ContentTypeName = "application/json";

    private readonly JsonSerializerOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value.Serializer;

    /// <inheritdoc />
    public string ContentType => ContentTypeName;

    /// <inheritdoc />
    public byte[] Serialize(object message, Type messageType) {
        Guard.NotNull(messageType);

        return JsonSerializer.SerializeToUtf8Bytes(message, messageType, _options);
    }

    /// <inheritdoc />
    public object? Deserialize(ReadOnlySpan<byte> payload, Type messageType) {
        Guard.NotNull(messageType);

        return JsonSerializer.Deserialize(payload, messageType, _options);
    }
}