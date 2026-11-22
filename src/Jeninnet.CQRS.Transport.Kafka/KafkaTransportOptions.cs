//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Kafka;

/// <summary>
/// Producer, consumer-group, topic, and partitioning options for the Kafka transport.
/// </summary>
public sealed class KafkaTransportOptions {
    /// <summary>Gets or sets the bootstrap servers. Defaults to <c>localhost:9092</c>.</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>Gets or sets the base consumer group id. Defaults to <c>cqrs</c>.</summary>
    public string ConsumerGroup { get; set; } = "cqrs";

    /// <summary>Gets or sets the client id reported to the broker.</summary>
    public string? ClientId { get; set; }

    /// <summary>Gets or sets whether topics are created on start when missing. Defaults to <see langword="true"/>.</summary>
    public bool EnsureTopics { get; set; } = true;

    /// <summary>Gets or sets the partition count used when creating topics. Defaults to 3.</summary>
    public int NumPartitions { get; set; } = 3;

    /// <summary>Gets or sets the replication factor used when creating topics. Defaults to 1.</summary>
    public short ReplicationFactor { get; set; } = 1;

    /// <summary>
    /// Gets or sets whether the producer enables idempotence (deduplicated retries per partition).
    /// Defaults to <see langword="true"/>; this is not Kafka transactions and does not provide
    /// exactly-once handler effects.
    /// </summary>
    public bool EnableIdempotence { get; set; } = true;

    /// <summary>
    /// Gets or sets the delay after a handler failure before the loop continues. The failed
    /// offset stays uncommitted and is redelivered after restart/rebalance. Defaults to 500 ms.
    /// </summary>
    public TimeSpan HandlerFailureDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Gets the message types this transport consumes.</summary>
    public IList<KafkaSubscription> Subscriptions { get; } = [];
}