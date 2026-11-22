//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Per-message envelope metadata supplied by the publisher. Everything is optional; the
/// transport generates a message id and timestamp when they are omitted.
/// </summary>
public sealed class TransportMessageOptions {
    /// <summary>Gets or sets an explicit message id; defaults to a generated value.</summary>
    public string? MessageId { get; set; }

    /// <summary>Gets or sets the correlation id shared by a logical workflow.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Gets or sets the id of the message that caused this one.</summary>
    public string? CausationId { get; set; }

    /// <summary>Gets or sets the tenant partition key.</summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Gets or sets the broker partition key. Ordered transports (Kafka) hash it to a partition;
    /// a stable key preserves per-aggregate ordering.
    /// </summary>
    public string? PartitionKey { get; set; }

    /// <summary>Gets or sets transport-neutral headers; the transport copies them into the envelope.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// Gets or sets the message schema version carried on the wire. Defaults to 1. The transport
    /// rejects versions below 1 so consumers never see an ambiguous schema.
    /// </summary>
    public int MessageVersion { get; set; } = 1;

    /// <summary>
    /// Gets or sets when the broker should enqueue the message (native scheduled delivery).
    /// Transports without a scheduler ignore it; Azure Service Bus uses
    /// <c>ScheduledEnqueueTime</c>. <see langword="null"/> means immediate delivery.
    /// </summary>
    public DateTimeOffset? ScheduledEnqueueTimeUtc { get; set; }
}
