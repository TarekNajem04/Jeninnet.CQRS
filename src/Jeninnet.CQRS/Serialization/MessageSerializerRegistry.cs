//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Serialization;

/// <summary>
/// Content-type registry used on the receive side. The default serializer handles outgoing
/// payloads; optional format packages contribute additional <see cref="MessageSerializerFormat"/>
/// registrations, and the transport envelope's content type selects the reader.
/// </summary>
public sealed class MessageSerializerRegistry : IMessageSerializerRegistry {
    private readonly Dictionary<string, IMessageSerializer> _serializers;
    private readonly string[] _contentTypes;

    /// <summary>Initializes the registry.</summary>
    /// <param name="defaultSerializer">The serializer used for sends and for payloads without a content type.</param>
    /// <param name="formats">Additional registered formats; duplicates resolve to the last registration.</param>
    /// <exception cref="ArgumentException">A format declares an empty content type.</exception>
    public MessageSerializerRegistry(
        IMessageSerializer defaultSerializer,
        IEnumerable<MessageSerializerFormat>? formats = null) {
        Default = defaultSerializer ?? throw new ArgumentNullException(nameof(defaultSerializer));

        _serializers = new Dictionary<string, IMessageSerializer>(StringComparer.OrdinalIgnoreCase);

        foreach(var format in formats ?? []) {
            Guard.NotNull(format);

            var contentType = Normalize(format.ContentType);

            if(contentType.Length is 0) {
                throw new ArgumentException("Every message serializer format must declare a content type.", nameof(formats));
            }

            _serializers[contentType] = format.Serializer;
        }

        var defaultContentType = Normalize(Default.ContentType);

        if(defaultContentType.Length is 0) {
            throw new ArgumentException("The default message serializer must declare a content type.", nameof(defaultSerializer));
        }

        // The default wins over a format that claims the same content type.
        _serializers[defaultContentType] = Default;
        _contentTypes = [.. _serializers.Keys.OrderBy(static key => key, StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public IMessageSerializer Default { get; }

    /// <inheritdoc />
    public IReadOnlyList<string> ContentTypes => _contentTypes;

    /// <inheritdoc />
    public IMessageSerializer Resolve(string? contentType) {
        var normalized = Normalize(contentType);

        if(normalized.Length is 0) {
            return Default;
        }

        return _serializers.TryGetValue(normalized, out var serializer)
            ? serializer
            : throw new UnknownContentTypeException(normalized, _contentTypes);
    }

    /// <inheritdoc />
    public bool TryResolve(string? contentType, [NotNullWhen(true)] out IMessageSerializer? serializer) {
        var normalized = Normalize(contentType);

        if(normalized.Length is 0) {
            serializer = Default;

            return true;
        }

        return _serializers.TryGetValue(normalized, out serializer);
    }

    /// <inheritdoc />
    public object? Deserialize(ReadOnlySpan<byte> payload, Type messageType, string? contentType) {
        Guard.NotNull(messageType);

        return Resolve(contentType).Deserialize(payload, messageType);
    }

    /// <summary>Strips media-type parameters and normalizes casing and whitespace.</summary>
    /// <param name="contentType">The wire content type.</param>
    internal static string Normalize(string? contentType) {
        if(StringCompat.IsNullOrWhiteSpace(contentType)) {
            return string.Empty;
        }

        var separator = contentType.IndexOf(';');
        var mediaType = separator >= 0 ? contentType.Substring(0, separator) : contentType;

        return mediaType.Trim().ToLowerInvariant();
    }
}