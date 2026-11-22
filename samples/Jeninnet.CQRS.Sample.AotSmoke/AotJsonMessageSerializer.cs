//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//

namespace Jeninnet.CQRS.Sample.AotSmoke;

/// <summary>
/// AOT-safe <see cref="IMessageSerializer"/>: every payload format is declared on a source-generated
/// <see cref="JsonSerializerContext"/>, so trimming removes nothing the durable store needs.
/// </summary>
public sealed class AotJsonMessageSerializer : IMessageSerializer {
    /// <inheritdoc />
    public string ContentType => "application/json";

    /// <inheritdoc />
    public byte[] Serialize(object message, Type messageType) {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(messageType);

        return JsonSerializer.SerializeToUtf8Bytes(message, messageType, AotJsonContext.Default);
    }

    /// <inheritdoc />
    public object? Deserialize(ReadOnlySpan<byte> payload, Type messageType) {
        ArgumentNullException.ThrowIfNull(messageType);

        return JsonSerializer.Deserialize(payload, messageType, AotJsonContext.Default);
    }
}

/// <summary>The closed JSON metadata the trimmed smoke publishes.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(AotDeferredCommand))]
[JsonSerializable(typeof(AotDeferredResultCommand))]
[JsonSerializable(typeof(PersistedDeferredCommand))]
internal sealed partial class AotJsonContext : JsonSerializerContext;