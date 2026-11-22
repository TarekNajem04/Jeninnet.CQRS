//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Kafka;

/// <summary>
/// A message type this transport consumes. Each subscription resolves to one Kafka topic by the
/// naming convention or <see cref="Topic"/>.
/// </summary>
public sealed class KafkaSubscription {
    /// <summary>Gets or sets the message type to consume.</summary>
    public Type MessageType { get; set; } = typeof(object);

    /// <summary>Gets or sets an explicit topic name; <see langword="null"/> uses the naming convention.</summary>
    public string? Topic { get; set; }
}