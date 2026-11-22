//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Http;

/// <summary>
/// Header names used by the HTTP adapter in addition to the core
/// <see cref="TransportHeaders"/> metadata. Custom envelope headers travel with the
/// <see cref="HeaderPrefix"/> prefix so they cannot collide with transport metadata.
/// </summary>
public static class HttpTransportHeaders {
    /// <summary>The unique message id.</summary>
    public const string MessageId = "x-cqrs-message-id";

    /// <summary>The correlation id.</summary>
    public const string CorrelationId = "x-cqrs-correlation-id";

    /// <summary>The causation id.</summary>
    public const string CausationId = "x-cqrs-causation-id";

    /// <summary>The partition key.</summary>
    public const string PartitionKey = "x-cqrs-partition-key";

    /// <summary>The delivery mode (<c>send</c> or <c>publish</c>).</summary>
    public const string DeliveryMode = "x-cqrs-delivery-mode";

    /// <summary>The UTC envelope timestamp in round-trip format.</summary>
    public const string Timestamp = "x-cqrs-timestamp";

    /// <summary>The fan-out exchange name.</summary>
    public const string Exchange = "x-cqrs-exchange";

    /// <summary>The point-to-point queue name.</summary>
    public const string Queue = "x-cqrs-queue";

    /// <summary>The topic name.</summary>
    public const string Topic = "x-cqrs-topic";

    /// <summary>The routing key.</summary>
    public const string RoutingKey = "x-cqrs-routing-key";

    /// <summary>The prefix applied to caller-supplied envelope headers.</summary>
    public const string HeaderPrefix = "x-cqrs-header-";

    /// <summary>The HMAC-SHA256 signature header.</summary>
    public const string Signature = "x-cqrs-signature";

    /// <summary>The default API-key header.</summary>
    public const string ApiKey = "x-api-key";
}
