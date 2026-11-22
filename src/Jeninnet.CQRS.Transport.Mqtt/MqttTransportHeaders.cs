//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Mqtt;

/// <summary>Header/user-property names used by the MQTT adapter.</summary>
public static class MqttTransportHeaders {
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

    /// <summary>The prefix applied to caller-supplied headers.</summary>
    public const string HeaderPrefix = "x-cqrs-header-";
}

/// <summary>Thrown when the MQTT broker rejects a publish or subscription.</summary>
public sealed class MqttTransportException : InvalidOperationException {
    /// <summary>Initializes the exception.</summary>
    /// <param name="message">The human-readable failure.</param>
    /// <param name="reasonCode">The broker reason code, when available.</param>
    /// <param name="innerException">The underlying failure, when available.</param>
    public MqttTransportException(string message, string? reasonCode = null, Exception? innerException = null)
        : base(message, innerException) {
        ReasonCode = reasonCode;
    }

    /// <summary>Gets the broker reason code, when available.</summary>
    public string? ReasonCode { get; }
}
