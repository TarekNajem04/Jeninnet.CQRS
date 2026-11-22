//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Serialization.NewtonsoftJson;

/// <summary>
/// Newtonsoft.Json <see cref="IMessageSerializer"/> for external and legacy services that produce
/// or consume <c>application/x-newtonsoft-json</c>. The default web JSON contract is camelCase,
/// matching the core <c>System.Text.Json</c> payloads.
/// </summary>
/// <remarks>
/// Newtonsoft.Json uses reflection and JIT-generated converters; this format is intended for
/// interop, not for native AOT publishing. Prefer the default JSON serializer or MessagePack
/// with source-generated formatters when publishing trimmed or AOT applications.
/// </remarks>
/// <param name="options">The Newtonsoft.Json options. Never <see langword="null"/>.</param>
public sealed class NewtonsoftJsonMessageSerializer(IOptions<NewtonsoftJsonMessageSerializerOptions> options) : IMessageSerializer {
    /// <summary>The content type this serializer declares and reads.</summary>
    public const string ContentTypeName = "application/x-newtonsoft-json";

    private readonly JsonSerializerSettings _settings = (options ?? throw new ArgumentNullException(nameof(options))).Value.Serializer;

    /// <inheritdoc />
    public string ContentType => ContentTypeName;

    /// <inheritdoc />
    public byte[] Serialize(object message, Type messageType) {
        Guard.NotNull(message);
        Guard.NotNull(messageType);

        return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message, messageType, _settings));
    }

    /// <inheritdoc />
    public object? Deserialize(ReadOnlySpan<byte> payload, Type messageType) {
        Guard.NotNull(messageType);

        return JsonConvert.DeserializeObject(EncodingCompat.GetUtf8String(payload), messageType, _settings);
    }
}
