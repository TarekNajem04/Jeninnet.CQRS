//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.SignalR;

/// <summary>Hub method and wire names shared by the client transport and the relay hub.</summary>
public static class SignalRTransportProtocol {
    /// <summary>The client callback that receives a serialized envelope.</summary>
    public const string DeliveryMethod = "cqrs-delivery";

    /// <summary>The hub method a client invokes for point-to-point delivery.</summary>
    public const string SendMethod = "Send";

    /// <summary>The hub method a client invokes to fan out to all connected clients.</summary>
    public const string PublishMethod = "Publish";
}

/// <summary>
/// The SignalR wire projection of <see cref="TransportEnvelope"/>. Payload bytes travel as a
/// base64 JSON value; the topology and metadata stay explicit so the receiver can rebuild the
/// canonical envelope without knowing the sender's process.
/// </summary>
/// <param name="MessageId">The unique message id.</param>
/// <param name="MessageType">The assembly-qualified or full message type name.</param>
/// <param name="ContentType">The payload media type.</param>
/// <param name="Payload">The serialized payload bytes.</param>
/// <param name="CorrelationId">The caller's correlation id, when supplied.</param>
/// <param name="CausationId">The id of the causing message, when supplied.</param>
/// <param name="TenantId">The tenant partition key, when supplied.</param>
/// <param name="PartitionKey">The broker partition key, when supplied.</param>
/// <param name="Timestamp">When the transport accepted the message, in UTC.</param>
/// <param name="Headers">Transport-neutral string headers; never <see langword="null"/>.</param>
/// <param name="Exchange">The fan-out exchange name.</param>
/// <param name="Queue">The point-to-point queue name.</param>
/// <param name="Topic">The topic name.</param>
/// <param name="RoutingKey">The routing key.</param>
/// <param name="DeliveryMode"><c>send</c> or <c>publish</c>.</param>
/// <param name="MessageVersion">The schema version carried on the wire.</param>
public sealed record SignalRWireEnvelope(
    string MessageId,
    string MessageType,
    string ContentType,
    byte[] Payload,
    string? CorrelationId,
    string? CausationId,
    string? TenantId,
    string? PartitionKey,
    DateTimeOffset Timestamp,
    Dictionary<string, string> Headers,
    string Exchange,
    string Queue,
    string Topic,
    string RoutingKey,
    string DeliveryMode,
    int MessageVersion);