//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.AzureServiceBus;

/// <summary>
/// A message type this transport consumes. Send uses its queue; publish uses its topic and the
/// handler-specific subscriptions.
/// </summary>
public sealed class AzureServiceBusSubscription {
    /// <summary>Gets or sets the message type to consume.</summary>
    public Type MessageType { get; set; } = typeof(object);

    /// <summary>Gets or sets an explicit queue name; <see langword="null"/> uses the naming convention.</summary>
    public string? QueueName { get; set; }

    /// <summary>Gets or sets an explicit topic name; <see langword="null"/> uses the naming convention.</summary>
    public string? TopicName { get; set; }
}