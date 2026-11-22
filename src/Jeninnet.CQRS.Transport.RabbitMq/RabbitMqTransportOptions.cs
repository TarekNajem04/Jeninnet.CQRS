//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.RabbitMq;

/// <summary>
/// Connection, topology, consumption, and reconnection options for the RabbitMQ transport.
/// </summary>
public sealed class RabbitMqTransportOptions {
    /// <summary>Gets or sets the broker host. Defaults to <c>localhost</c>.</summary>
    public string HostName { get; set; } = "localhost";

    /// <summary>Gets or sets the AMQP port. Defaults to 5672.</summary>
    public int Port { get; set; } = 5672;

    /// <summary>Gets or sets the user name. Defaults to <c>guest</c>.</summary>
    public string UserName { get; set; } = "guest";

    /// <summary>Gets or sets the password. Defaults to <c>guest</c>.</summary>
    public string Password { get; set; } = "guest";

    /// <summary>Gets or sets the virtual host. Defaults to <c>/</c>.</summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>Gets or sets the name shown in the RabbitMQ management UI.</summary>
    public string? ClientProvidedName { get; set; }

    /// <summary>Gets or sets whether exchanges and queues survive broker restarts. Defaults to <see langword="true"/>.</summary>
    public bool Durable { get; set; } = true;

    /// <summary>
    /// Gets or sets whether queues are declared with a dead-letter exchange and queue.
    /// Defaults to <see langword="true"/>; rejected deliveries go to <c>{queue}.dlq</c>.
    /// </summary>
    public bool EnableDeadLetter { get; set; } = true;

    /// <summary>
    /// Gets or sets whether queues use the quorum queue type. Defaults to <see langword="false"/>;
    /// quorum queues require a durable queue and a RabbitMQ 3.8+ broker.
    /// </summary>
    public bool UseQuorumQueues { get; set; }

    /// <summary>
    /// Gets or sets whether the publish channel waits for broker confirms. Defaults to
    /// <see langword="true"/>; a nack or unroutable mandatory publish throws
    /// <c>PublishException</c> so the outbox relay can leave the row pending.
    /// </summary>
    public bool PublisherConfirms { get; set; } = true;

    /// <summary>Gets or sets the consumer prefetch count. Defaults to 16.</summary>
    public ushort PrefetchCount { get; set; } = 16;

    /// <summary>Gets or sets the consumer dispatch concurrency. Defaults to 1 (ordered delivery).</summary>
    public ushort ConsumerDispatchConcurrency { get; set; } = 1;

    /// <summary>Gets or sets the exchange type. Defaults to <c>fanout</c>.</summary>
    public string ExchangeType { get; set; } = "fanout";

    /// <summary>
    /// Gets or sets the first reconnect delay after the broker connection disappears. Defaults
    /// to one second; the delay doubles up to <see cref="MaxReconnectDelay"/>.
    /// </summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets the reconnect backoff ceiling. Defaults to 30 seconds.</summary>
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets the message types this transport consumes.</summary>
    public IList<RabbitMqSubscription> Subscriptions { get; } = [];
}
