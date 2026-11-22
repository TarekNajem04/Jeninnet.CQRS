//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.RabbitMq;

/// <summary>
/// A message type this transport consumes. Each subscription declares its exchange (fan-out) and
/// durable queue; handlers are attached when the transport starts.
/// </summary>
public sealed class RabbitMqSubscription {
    /// <summary>Gets or sets the message type to consume.</summary>
    public Type MessageType { get; set; } = typeof(object);

    /// <summary>
    /// Gets or sets an explicit durable queue name; when <see langword="null"/> the naming
    /// convention's queue is used.
    /// </summary>
    public string? QueueName { get; set; }
}