//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>Whether a message is routed point-to-point or fanned out.</summary>
public enum TransportDeliveryMode {
    /// <summary>One consumer receives the message.</summary>
    Send = 0,

    /// <summary>Every subscriber receives the message.</summary>
    Publish = 1,
}

/// <summary>
/// The canonical broker-neutral envelope. The transport owns creation; consumers use the metadata
/// and payload without depending on any broker's message type.
/// </summary>
/// <param name="MessageId">The unique message id (assigned by the transport when absent).</param>
/// <param name="MessageType">The assembly-qualified or full message type name.</param>
/// <param name="ContentType">The payload content type from the serializer.</param>
/// <param name="Payload">The serialized message body.</param>
/// <param name="CorrelationId">The caller's correlation id, when supplied.</param>
/// <param name="CausationId">The id of the message that caused this one, when supplied.</param>
/// <param name="TenantId">The tenant partition key, when supplied.</param>
/// <param name="PartitionKey">The broker partition key (for ordered transports such as Kafka), when supplied.</param>
/// <param name="Timestamp">When the transport accepted the message, in UTC.</param>
/// <param name="Headers">Transport-neutral string headers; never <see langword="null"/>.</param>
/// <param name="Topology">The exchange/queue/topic names resolved for the message type.</param>
/// <param name="DeliveryMode">Send or publish.</param>
public sealed record TransportEnvelope(
    string MessageId,
    string MessageType,
    string ContentType,
    ReadOnlyMemory<byte> Payload,
    string? CorrelationId,
    string? CausationId,
    string? TenantId,
    string? PartitionKey,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, string> Headers,
    TransportTopology Topology,
    TransportDeliveryMode DeliveryMode) {
    /// <summary>
    /// Gets the message schema version declared on the wire. Defaults to 1; consumers use it to
    /// apply <see cref="Versioning.MessageUpgradePolicy"/> before dispatch.
    /// </summary>
    public int MessageVersion { get; init; } = 1;
}