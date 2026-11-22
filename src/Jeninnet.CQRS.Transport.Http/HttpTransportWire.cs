//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Http;

/// <summary>
/// Converts between the canonical <see cref="TransportEnvelope"/> and the HTTP wire format.
/// Receivers can use <see cref="ReadEnvelope"/> instead of reimplementing the header contract.
/// </summary>
public static class HttpTransportWire {
    /// <summary>
    /// Builds the HTTP header set for an envelope. Well-known metadata keeps its canonical header
    /// name; caller-supplied headers are prefixed with <see cref="HttpTransportHeaders.HeaderPrefix"/>.
    /// </summary>
    /// <param name="envelope">The envelope to project.</param>
    /// <returns>A case-insensitive header map.</returns>
    public static IReadOnlyDictionary<string, string> CreateHeaders(TransportEnvelope envelope) {
        Guard.NotNull(envelope);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            [HttpTransportHeaders.MessageId] = envelope.MessageId,
            [HttpTransportHeaders.DeliveryMode] = envelope.DeliveryMode is TransportDeliveryMode.Send ? "send" : "publish",
            [HttpTransportHeaders.Timestamp] = envelope.Timestamp.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            [HttpTransportHeaders.Exchange] = envelope.Topology.Exchange,
            [HttpTransportHeaders.Queue] = envelope.Topology.Queue,
            [HttpTransportHeaders.Topic] = envelope.Topology.Topic,
            [HttpTransportHeaders.RoutingKey] = envelope.Topology.RoutingKey,
        };

        TransportHeaders.SetMetadata(headers, envelope.MessageType, envelope.MessageVersion, envelope.ContentType);

        if(!StringCompat.IsNullOrWhiteSpace(envelope.CorrelationId)) {
            headers[HttpTransportHeaders.CorrelationId] = envelope.CorrelationId;
        }

        if(!StringCompat.IsNullOrWhiteSpace(envelope.CausationId)) {
            headers[HttpTransportHeaders.CausationId] = envelope.CausationId;
        }

        if(!StringCompat.IsNullOrWhiteSpace(envelope.TenantId)) {
            headers[HttpTransportHeaders.PartitionKey] = envelope.PartitionKey ?? string.Empty;
            headers[TransportHeaders.Tenant] = envelope.TenantId;
        } else if(!StringCompat.IsNullOrWhiteSpace(envelope.PartitionKey)) {
            headers[HttpTransportHeaders.PartitionKey] = envelope.PartitionKey;
        }

        foreach(var pair in envelope.Headers) {
            headers[HttpTransportHeaders.HeaderPrefix + pair.Key] = pair.Value;
        }

        return headers;
    }

    /// <summary>
    /// Reconstructs the canonical envelope from HTTP headers and a payload body.
    /// </summary>
    /// <param name="headers">The request headers, including content headers.</param>
    /// <param name="payload">The request body bytes.</param>
    /// <param name="namingConvention">
    /// Optional fallback used when the sender omitted topology headers. When omitted, the sender's
    /// topology is used as-is.
    /// </param>
    /// <returns>The reconstructed envelope.</returns>
    /// <exception cref="FormatException">The message type header is missing or empty.</exception>
    public static TransportEnvelope ReadEnvelope(
        IReadOnlyDictionary<string, string> headers,
        byte[] payload,
        TransportNamingConvention? namingConvention = null) {
        Guard.NotNull(headers);
        Guard.NotNull(payload);

        var normalized = new Dictionary<string, string>(headers.Count, StringComparer.OrdinalIgnoreCase);

        foreach(var pair in headers) {
            normalized[pair.Key] = pair.Value;
        }

        var messageType = TransportHeaders.GetMessageType(normalized);

        if(StringCompat.IsNullOrWhiteSpace(messageType)) {
            throw new FormatException("The HTTP request is missing the message-type header.");
        }

        var messageVersion = TransportHeaders.GetMessageVersion(normalized);
        var contentType = TransportHeaders.GetContentType(normalized) ?? "application/octet-stream";
        var envelopeHeaders = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach(var pair in normalized) { // NOSONAR: Explicit loop is clearer and faster than a LINQ pipeline here.
            if(pair.Key.StartsWith(HttpTransportHeaders.HeaderPrefix, StringComparison.OrdinalIgnoreCase)) {
                envelopeHeaders[pair.Key.Substring(HttpTransportHeaders.HeaderPrefix.Length)] = pair.Value;
            }
        }

        TransportHeaders.SetMetadata(envelopeHeaders, messageType, messageVersion, contentType);

        var tenantId = normalized.TryGetValue(TransportHeaders.Tenant, out var tenant) && !StringCompat.IsNullOrWhiteSpace(tenant)
            ? tenant
            : null;

        if(tenantId is not null) {
            envelopeHeaders[TransportHeaders.Tenant] = tenantId;
        }

        var topology = ReadTopology(normalized, messageType, namingConvention);
        var timestamp = normalized.TryGetValue(HttpTransportHeaders.Timestamp, out var timestampText) &&
                        DateTimeOffset.TryParse(timestampText, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;
        var deliveryMode = normalized.TryGetValue(HttpTransportHeaders.DeliveryMode, out var mode) &&
                           string.Equals(mode, "send", StringComparison.OrdinalIgnoreCase)
            ? TransportDeliveryMode.Send
            : TransportDeliveryMode.Publish;

        return new TransportEnvelope(
            normalized.TryGetValue(HttpTransportHeaders.MessageId, out var messageId) && !StringCompat.IsNullOrWhiteSpace(messageId)
                ? messageId
                : Guid.NewGuid().ToString("N"),
            messageType,
            contentType,
            payload,
            ReadOptional(normalized, HttpTransportHeaders.CorrelationId),
            ReadOptional(normalized, HttpTransportHeaders.CausationId),
            tenantId,
            ReadOptional(normalized, HttpTransportHeaders.PartitionKey),
            timestamp,
            envelopeHeaders,
            topology,
            deliveryMode) {
            MessageVersion = messageVersion,
        };
    }

    /// <summary>
    /// Verifies the HMAC-SHA256 signature of a received body. The comparison accepts either case.
    /// </summary>
    /// <param name="headers">The request headers.</param>
    /// <param name="payload">The raw request body.</param>
    /// <param name="secret">The shared secret.</param>
    /// <param name="headerName">The signature header name; defaults to <see cref="HttpTransportHeaders.Signature"/>.</param>
    /// <returns><see langword="true"/> when the header matches the computed signature.</returns>
    public static bool VerifySignature(
        IReadOnlyDictionary<string, string> headers,
        byte[] payload,
        string secret,
        string headerName = HttpTransportHeaders.Signature) {
        Guard.NotNull(headers);
        Guard.NotNull(payload);
        Guard.NotNullOrWhiteSpace(secret);
        Guard.NotNullOrWhiteSpace(headerName);

        if(!TryRead(headers, headerName, out var provided) || StringCompat.IsNullOrWhiteSpace(provided)) {
            return false;
        }

        var expected = ComputeSignature(payload, secret);

        return FixedTimeEquals(provided, expected);
    }

    private static bool FixedTimeEquals(string left, string right) {
        if(left.Length != right.Length) {
            return false;
        }

        var difference = 0;

        for(var index = 0; index < left.Length; index++) {
            difference |= char.ToUpperInvariant(left[index]) ^ char.ToUpperInvariant(right[index]);
        }

        return difference is 0;
    }

    internal static string ComputeSignature(byte[] payload, string secret) {
        Guard.NotNull(payload);
        Guard.NotNullOrWhiteSpace(secret);

        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(payload);

#if NETSTANDARD2_0
        return "sha256=" + BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
#else
        return "sha256=" + Convert.ToHexStringLower(hash);
#endif
    }

    private static string? ReadOptional(IReadOnlyDictionary<string, string> headers, string name) =>
        headers.TryGetValue(name, out var value) && !StringCompat.IsNullOrWhiteSpace(value) ? value : null;

    private static bool TryRead(IReadOnlyDictionary<string, string> headers, string name, out string? value) {
        foreach(var pair in headers) { // NOSONAR: Explicit loop is clearer and faster than a LINQ pipeline here.
            if(string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) {
                value = pair.Value;

                return true;
            }
        }

        value = null;

        return false;
    }

    private static TransportTopology ReadTopology(
        IReadOnlyDictionary<string, string> headers,
        string messageType,
        TransportNamingConvention? namingConvention) {
        var exchange = ReadOptional(headers, HttpTransportHeaders.Exchange);
        var queue = ReadOptional(headers, HttpTransportHeaders.Queue);
        var topic = ReadOptional(headers, HttpTransportHeaders.Topic);
        var routingKey = ReadOptional(headers, HttpTransportHeaders.RoutingKey);

        if(exchange is not null && queue is not null && topic is not null && routingKey is not null) {
            return new TransportTopology(exchange, queue, topic, routingKey);
        }

        var resolvedType = Type.GetType(messageType, throwOnError: false);

        if(resolvedType is not null && namingConvention is not null) {
            return namingConvention.Describe(resolvedType);
        }

        return new TransportTopology(exchange ?? string.Empty, queue ?? string.Empty, topic ?? string.Empty, routingKey ?? string.Empty);
    }
}