//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Well-known transport-neutral metadata headers. Broker adapters write these on every message
/// and prefer them on receive, with the legacy <c>x-message-type</c> header accepted for
/// messages produced before the metadata was standardized.
/// </summary>
public static class TransportHeaders {
    /// <summary>The assembly-qualified or full message type name.</summary>
    public const string MessageType = "message-type";

    /// <summary>The integer message schema version; defaults to 1 when absent.</summary>
    public const string MessageVersion = "message-version";

    /// <summary>The payload media type negotiated by the consuming serializer.</summary>
    public const string ContentType = "content-type";

    /// <summary>The tenant partition key carried across the wire.</summary>
    public const string Tenant = "x-tenant-id";

    /// <summary>The historical message type header emitted before the metadata was standardized.</summary>
    public const string LegacyMessageType = "x-message-type";

    /// <summary>
    /// Writes the standard metadata headers, overriding caller-supplied values for the same keys.
    /// </summary>
    /// <param name="headers">The mutable envelope headers.</param>
    /// <param name="messageType">The assembly-qualified or full message type name.</param>
    /// <param name="messageVersion">The schema version; must be at least 1.</param>
    /// <param name="contentType">The serializer content type.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="messageVersion"/> is less than 1.</exception>
    public static void SetMetadata(IDictionary<string, string> headers, string messageType, int messageVersion, string contentType) {
        Guard.NotNull(headers);
        Guard.NotNullOrWhiteSpace(messageType);
        Guard.NotNullOrWhiteSpace(contentType);

        if(messageVersion < 1) {
            throw new ArgumentOutOfRangeException(nameof(messageVersion), messageVersion, "Message version must be at least 1.");
        }

        headers[MessageType] = messageType;
        headers[MessageVersion] = messageVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        headers[ContentType] = contentType;
    }

    /// <summary>Reads the message type, accepting the legacy <c>x-message-type</c> header.</summary>
    /// <param name="headers">The envelope headers.</param>
    public static string? GetMessageType(IReadOnlyDictionary<string, string> headers) {
        Guard.NotNull(headers);

        if(headers.TryGetValue(MessageType, out var messageType) && !StringCompat.IsNullOrWhiteSpace(messageType)) {
            return messageType;
        }

        return headers.TryGetValue(LegacyMessageType, out var legacy) && !StringCompat.IsNullOrWhiteSpace(legacy)
            ? legacy
            : null;
    }

    /// <summary>Reads the content type from the standard header.</summary>
    /// <param name="headers">The envelope headers.</param>
    public static string? GetContentType(IReadOnlyDictionary<string, string> headers) {
        Guard.NotNull(headers);

        return headers.TryGetValue(ContentType, out var contentType) && !StringCompat.IsNullOrWhiteSpace(contentType)
            ? contentType
            : null;
    }

    /// <summary>
    /// Reads the schema version; missing or malformed versions fall back to 1 so legacy messages
    /// stay readable, while outgoing versions are validated by <see cref="SetMetadata"/>.
    /// </summary>
    /// <param name="headers">The envelope headers.</param>
    public static int GetMessageVersion(IReadOnlyDictionary<string, string> headers) {
        Guard.NotNull(headers);

        return headers.TryGetValue(MessageVersion, out var value) &&
               int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var version) &&
               version >= 1
            ? version
            : 1;
    }
}
