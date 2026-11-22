//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//

namespace Jeninnet.CQRS.Transport.Aws;

/// <summary>The AWS wire record: envelope metadata plus inline Base64 or a claim-check reference.</summary>
internal sealed record AwsTransportBody(
    string MessageId,
    string MessageType,
    int MessageVersion,
    string ContentType,
    string? CorrelationId,
    string? CausationId,
    string? TenantId,
    string? PartitionKey,
    IReadOnlyDictionary<string, string> Headers,
    string? Payload,
    string? ClaimCheckKey);

/// <summary>Encodes and decodes the SQS/SNS body, enforcing the AWS payload ceiling.</summary>
internal static class AwsMessageCodec {
    internal const string DeliveryModeHeader = "x-cqrs-delivery-mode";
    internal const string MessageIdHeader = "x-message-id";
    internal const string CorrelationHeader = "x-correlation-id";
    internal const string CausationHeader = "x-causation-id";
    internal const string TenantHeader = "x-tenant-id";
    internal const string PartitionKeyHeader = "x-partition-key";

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    internal static async Task<string> EncodeAsync(
        TransportEnvelope envelope,
        byte[] payload,
        ClaimCheckPolicy claimChecks,
        int payloadLimitBytes,
        CancellationToken cancellationToken) {
        var claimCheckKey = await claimChecks.StageIfLargeAsync(payload, envelope.ContentType, cancellationToken).ConfigureAwait(false);
        var body = new AwsTransportBody(
            envelope.MessageId,
            envelope.MessageType,
            envelope.MessageVersion,
            envelope.ContentType,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.TenantId,
            envelope.PartitionKey,
            envelope.Headers,
            claimCheckKey is null ? Convert.ToBase64String(payload) : null,
            claimCheckKey);
        var json = JsonSerializer.Serialize(body, _json);

        if(Encoding.UTF8.GetByteCount(json) > payloadLimitBytes) {
            throw new InvalidOperationException(
                $"The AWS message body is {Encoding.UTF8.GetByteCount(json)} bytes, above the {payloadLimitBytes}-byte limit. Configure the claim-check threshold below the AWS cap so the payload is staged.");
        }

        return json;
    }

    internal static async Task<TransportEnvelope> DecodeAsync(
        string body,
        TransportTopology topology,
        DateTimeOffset timestamp,
        ClaimCheckPolicy claimChecks,
        IMessageSerializer serializer,
        CancellationToken cancellationToken) {
        var decoded = JsonSerializer.Deserialize<AwsTransportBody>(body, _json)
            ?? throw new InvalidOperationException("The AWS message body is empty or corrupt.");
        var payload = decoded.ClaimCheckKey is null
            ? Convert.FromBase64String(decoded.Payload ?? string.Empty)
            : await claimChecks.GetClaimAsync(decoded.ClaimCheckKey, decoded.ContentType, cancellationToken).ConfigureAwait(false)
              ?? throw new InvalidOperationException($"Claim-check '{decoded.ClaimCheckKey}' was not found.");

        var headers = decoded.Headers is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : decoded.Headers.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        var deliveryMode = headers.TryGetValue(DeliveryModeHeader, out var mode) && string.Equals(mode, "send", StringComparison.Ordinal)
            ? TransportDeliveryMode.Send
            : TransportDeliveryMode.Publish;
        var messageType = TransportHeaders.GetMessageType(headers) ?? decoded.MessageType;
        var contentType = TransportHeaders.GetContentType(headers) ?? decoded.ContentType;
        var messageVersion = decoded.MessageVersion >= 1
            ? decoded.MessageVersion
            : TransportHeaders.GetMessageVersion(headers);

        return new TransportEnvelope(
            decoded.MessageId,
            messageType,
            contentType,
            payload,
            decoded.CorrelationId,
            decoded.CausationId,
            decoded.TenantId,
            decoded.PartitionKey,
            timestamp,
            headers,
            topology,
            deliveryMode) {
            MessageVersion = messageVersion,
        };
    }

    internal static string Fit(string name, int maxLength = 80) {
        var builder = new StringBuilder(name.Length);

        foreach(var character in name) {
            builder.Append(character is >= 'a' and <= 'z' ||
                           character is >= 'A' and <= 'Z' ||
                           character is >= '0' and <= '9' ||
                           character is '-' or '_'
                ? character
                : '-');
        }

        var sanitized = builder.ToString();

        if(sanitized.Length <= maxLength) {
            return sanitized;
        }

        var hash = CryptoCompat.ToHex(CryptoCompat.Sha256(Encoding.UTF8.GetBytes(sanitized)))
            .Substring(0, 8)
            .ToLowerInvariant();

        return $"{sanitized.Substring(0, maxLength - 9)}-{hash}";
    }
}