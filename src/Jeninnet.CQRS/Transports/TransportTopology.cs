//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Broker-neutral topology names for one message type. A transport picks the names its broker
/// understands (AMQP uses exchange/queue/routing key, Kafka uses topic).
/// </summary>
/// <param name="Exchange">The fan-out exchange name.</param>
/// <param name="Queue">The point-to-point queue name.</param>
/// <param name="Topic">The topic name.</param>
/// <param name="RoutingKey">The routing key used for send/publish.</param>
public sealed record TransportTopology(string Exchange, string Queue, string Topic, string RoutingKey);
