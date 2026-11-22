//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Per-transport envelope builder. An adapter supplies only its tenant, topology, timestamp, and
/// header policy; cloning, metadata, message ids, and versioning stay in
/// <see cref="TransportEnvelopeFactory"/>.
/// </summary>
internal sealed class TransportEnvelopeBuilder {
    private readonly Jeninnet.CQRS.Serialization.IMessageSerializer _serializer;
    private readonly Func<Type, string?, TransportTopology> _topology;
    private readonly Func<TransportMessageOptions?, string?> _tenant;
    private readonly Func<DateTimeOffset> _timestamp;
    private readonly bool _brokerHeaders;
    private readonly bool _partitionFallsBackToTenant;

    /// <summary>Initializes the builder.</summary>
    /// <param name="serializer">The payload serializer.</param>
    /// <param name="topology">Resolves the topology for a message type and resolved tenant.</param>
    /// <param name="tenant">Resolves the effective tenant from the options and ambient context.</param>
    /// <param name="timestamp">Supplies the envelope timestamp.</param>
    /// <param name="brokerHeaders">Whether to emit broker metadata headers.</param>
    /// <param name="partitionFallsBackToTenant">Whether a missing partition key falls back to the tenant id.</param>
    public TransportEnvelopeBuilder(
        Jeninnet.CQRS.Serialization.IMessageSerializer serializer,
        Func<Type, string?, TransportTopology> topology,
        Func<TransportMessageOptions?, string?> tenant,
        Func<DateTimeOffset> timestamp,
        bool brokerHeaders = false,
        bool partitionFallsBackToTenant = false) {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _topology = topology ?? throw new ArgumentNullException(nameof(topology));
        _tenant = tenant ?? throw new ArgumentNullException(nameof(tenant));
        _timestamp = timestamp ?? throw new ArgumentNullException(nameof(timestamp));
        _brokerHeaders = brokerHeaders;
        _partitionFallsBackToTenant = partitionFallsBackToTenant;
    }

    /// <summary>Creates an envelope for this transport.</summary>
    /// <param name="message">The original message.</param>
    /// <param name="messageType">The runtime message type.</param>
    /// <param name="options">Optional per-message transport options.</param>
    /// <param name="deliveryMode">Send or publish.</param>
    /// <param name="topology">An explicit topology, or <see langword="null"/> to resolve it from the message type.</param>
    /// <returns>The populated envelope.</returns>
    public TransportEnvelope Create(
        object message,
        Type messageType,
        TransportMessageOptions? options,
        TransportDeliveryMode deliveryMode,
        TransportTopology? topology = null) {
        var tenantId = _tenant(options);
        var partitionKey = _partitionFallsBackToTenant && Jeninnet.CQRS.Polyfills.StringCompat.IsNullOrWhiteSpace(options?.PartitionKey)
            ? options?.TenantId
            : options?.PartitionKey;

        return TransportEnvelopeFactory.Create(_serializer, new TransportEnvelopeContext {
            Message = message,
            MessageType = messageType,
            Options = options,
            DeliveryMode = deliveryMode,
            Topology = topology ?? _topology(messageType, tenantId),
            Timestamp = _timestamp(),
            TenantId = tenantId,
            PartitionKey = partitionKey,
            BrokerHeaders = _brokerHeaders,
        });
    }
}

/// <summary>Factory methods for the common envelope-builder policies.</summary>
internal static class TransportEnvelopeBuilders {
    /// <summary>Creates a builder for a broker transport that emits metadata headers.</summary>
    /// <param name="serializer">The payload serializer.</param>
    /// <param name="topology">Resolves the topology for a message type.</param>
    /// <param name="partitionFallsBackToTenant">Whether a missing partition key falls back to the tenant id.</param>
    /// <returns>The configured builder.</returns>
    public static TransportEnvelopeBuilder ForBroker(
        Jeninnet.CQRS.Serialization.IMessageSerializer serializer,
        Func<Type, TransportTopology> topology,
        bool partitionFallsBackToTenant = true) =>
        new(
            serializer,
            (messageType, _) => topology(messageType),
            static options => options?.TenantId,
            static () => DateTimeOffset.UtcNow,
            brokerHeaders: true,
            partitionFallsBackToTenant: partitionFallsBackToTenant);

    /// <summary>Creates a builder that resolves the tenant from options or the ambient tenant.</summary>
    /// <param name="serializer">The payload serializer.</param>
    /// <param name="topology">Resolves the topology for a message type and resolved tenant.</param>
    /// <param name="tenant">Supplies the ambient tenant.</param>
    /// <param name="timestamp">Supplies the envelope timestamp.</param>
    /// <returns>The configured builder.</returns>
    public static TransportEnvelopeBuilder ForTenantAware(
        Jeninnet.CQRS.Serialization.IMessageSerializer serializer,
        Func<Type, string?, TransportTopology> topology,
        Func<string?> tenant,
        Func<DateTimeOffset> timestamp) =>
        new(
            serializer,
            topology,
            options => Jeninnet.CQRS.Polyfills.StringCompat.IsNullOrWhiteSpace(options?.TenantId)
                ? tenant()
                : options.TenantId,
            timestamp);
}