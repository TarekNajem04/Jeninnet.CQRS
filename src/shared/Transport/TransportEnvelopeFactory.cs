//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Builds outgoing envelopes for every transport. Header cloning, tenant resolution, metadata,
/// message ids, correlation headers, and schema versioning live here so adapters only supply the
/// topology and transport-specific tenant/partition values.
/// </summary>
internal static class TransportEnvelopeFactory {
    /// <summary>Creates an outgoing transport envelope from the supplied context.</summary>
    /// <param name="serializer">The payload serializer.</param>
    /// <param name="context">The message, topology, tenant, and delivery values.</param>
    /// <returns>The populated envelope.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The requested message version is less than 1.</exception>
    public static TransportEnvelope Create(Jeninnet.CQRS.Serialization.IMessageSerializer serializer, TransportEnvelopeContext context) {
        var contentType = serializer.ContentType;
        var headers = context.Options?.Headers is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : context.Options.Headers.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        var messageVersion = context.Options?.MessageVersion ?? 1;
        var messageTypeName = context.MessageType.AssemblyQualifiedName ?? context.MessageType.FullName ?? context.MessageType.Name;
        var messageId = Jeninnet.CQRS.Polyfills.StringCompat.IsNullOrWhiteSpace(context.Options?.MessageId)
            ? Guid.NewGuid().ToString("N")
            : context.Options.MessageId;

        if(context.BrokerHeaders) {
            headers[TransportEnvelopeHeaders.DeliveryMode] = context.DeliveryMode is TransportDeliveryMode.Send ? "send" : "publish";
        }

        TransportHeaders.SetMetadata(headers, messageTypeName, messageVersion, contentType);

        if(context.BrokerHeaders) {
            headers[TransportEnvelopeHeaders.MessageId] = messageId;

            if(!Jeninnet.CQRS.Polyfills.StringCompat.IsNullOrWhiteSpace(context.Options?.CorrelationId)) {
                headers[TransportEnvelopeHeaders.CorrelationId] = context.Options.CorrelationId;
            }

            if(!Jeninnet.CQRS.Polyfills.StringCompat.IsNullOrWhiteSpace(context.Options?.CausationId)) {
                headers[TransportEnvelopeHeaders.CausationId] = context.Options.CausationId;
            }

            if(!Jeninnet.CQRS.Polyfills.StringCompat.IsNullOrWhiteSpace(context.PartitionKey)) {
                headers[TransportEnvelopeHeaders.PartitionKey] = context.PartitionKey;
            }
        }

        if(!Jeninnet.CQRS.Polyfills.StringCompat.IsNullOrWhiteSpace(context.TenantId)) {
            headers[TransportHeaders.Tenant] = context.TenantId;
        }

        return new TransportEnvelope(
            messageId,
            messageTypeName,
            contentType,
            serializer.Serialize(context.Message, context.MessageType),
            context.Options?.CorrelationId,
            context.Options?.CausationId,
            context.TenantId,
            context.PartitionKey,
            context.Timestamp,
            headers,
            context.Topology,
            context.DeliveryMode) {
            MessageVersion = messageVersion,
        };
    }
}

/// <summary>The inputs for one outgoing envelope.</summary>
internal sealed class TransportEnvelopeContext {
    /// <summary>Gets the original message.</summary>
    public object Message { get; init; } = null!;

    /// <summary>Gets the runtime message type.</summary>
    public Type MessageType { get; init; } = null!;

    /// <summary>Gets the optional per-message transport options.</summary>
    public TransportMessageOptions? Options { get; init; }

    /// <summary>Gets the delivery mode.</summary>
    public TransportDeliveryMode DeliveryMode { get; init; }

    /// <summary>Gets the resolved broker topology.</summary>
    public TransportTopology Topology { get; init; } = null!;

    /// <summary>Gets when the envelope was created.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets the resolved tenant id, including any ambient tenant.</summary>
    public string? TenantId { get; init; }

    /// <summary>Gets the resolved partition key.</summary>
    public string? PartitionKey { get; init; }

    /// <summary>Gets whether to emit the broker metadata headers used by AWS, ASB, Kafka, and RabbitMQ.</summary>
    public bool BrokerHeaders { get; init; }
}

/// <summary>The broker metadata header names shared by the wire adapters.</summary>
internal static class TransportEnvelopeHeaders {
    /// <summary>The send/publish delivery mode.</summary>
    public const string DeliveryMode = "x-cqrs-delivery-mode";

    /// <summary>The message id.</summary>
    public const string MessageId = "x-message-id";

    /// <summary>The correlation id.</summary>
    public const string CorrelationId = "x-correlation-id";

    /// <summary>The causation id.</summary>
    public const string CausationId = "x-causation-id";

    /// <summary>The partition key.</summary>
    public const string PartitionKey = "x-partition-key";
}
