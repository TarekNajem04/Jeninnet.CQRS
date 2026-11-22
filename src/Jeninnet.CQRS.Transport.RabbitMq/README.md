# Jeninnet.CQRS.Transport.RabbitMq

RabbitMQ transport for `Jeninnet.CQRS`: the same handlers across processes with AMQP topology,
while the broker-free path stays intact.

```csharp
builder.Services.AddCqrsMessageSerialization();
builder.Services.AddSingleton<IMessageTransportHandler, OrderMessageHandler>();
builder.Services.AddCqrsRabbitMq(options => {
    options.HostName = "rabbitmq";
    options.UserName = "app";
    options.Password = "secret";
    options.Subscriptions.Add(new RabbitMqSubscription { MessageType = typeof(OrderCreated) });
});
```

`AddCqrsRabbitMq` selects `CqrsTransportMode.External`, registers the transport, and adds a hosted
service that owns its lifetime. User registrations made before it win (`TryAdd`).

## Topology

- One **fan-out exchange per message type**, named by `TransportNamingConvention`
  (`cqrs.{full-name}`).
- One **durable queue per consumer endpoint**; with multiple handlers, the first uses the base
  queue and the rest use `{base}.{index}` so `Send` remains point-to-point and `Publish` fans out.
- A **direct dead-letter exchange** `{exchange}.dlx` and queue `{queue}.dlq` per queue. A handler
  failure rejects without requeue, so the poison message lands in the DLQ.
- `UseQuorumQueues = true` adds `x-queue-type=quorum`; queues stay durable and non-exclusive.

`SendAsync` publishes through the default exchange to the queue; `PublishAsync` goes through the
type exchange. Both use `mandatory: true`.

## Confirms, outages, reconnection

Publisher confirms are enabled by default (`PublisherConfirms = true`) with confirm tracking:
`BasicPublishAsync` throws `PublishException` on a nack or unroutable message. A broker outage
therefore fails the publish instead of losing the row; the outbox relay (CQRS-47) leaves it
pending and retries.

The transport owns reconnection: `AutomaticRecoveryEnabled` is disabled and an exponential
backoff loop (`ReconnectDelay`, `MaxReconnectDelay`) rebuilds the connection, topology, and
consumers after a broker restart, without restarting the host. Publishes during the outage throw
`InvalidOperationException`; the relay decides when to retry. Delivery is at-least-once: the
consumer acks only after the handler succeeds, and an unacked message is redelivered.

## Options

| Option | Default | Purpose |
|---|---|---|
| `HostName` / `Port` | `localhost` / `5672` | Broker endpoint |
| `UserName` / `Password` / `VirtualHost` | `guest` / `guest` / `/` | Credentials |
| `Durable` | `true` | Durable exchanges/queues and persistent messages |
| `EnableDeadLetter` | `true` | Declare DLX/DLQ per queue |
| `UseQuorumQueues` | `false` | Quorum queue type |
| `PublisherConfirms` | `true` | Wait for confirm/nack on publish |
| `PrefetchCount` | `16` | Consumer QoS |
| `ConsumerDispatchConcurrency` | `1` | Ordered by default; higher values call handlers concurrently |
| `ExchangeType` | `fanout` | Exchange type for message exchanges |
| `ReconnectDelay` / `MaxReconnectDelay` | `1s` / `30s` | Exponential backoff bounds |

## Tests and frameworks

The package targets `net10.0` and `net11.0` because core does; the `netstandard2.0` asset arrives
with `CQRS-53` (RabbitMQ.Client already supports it). Integration tests use Testcontainers with
`rabbitmq:4.2-management` and the shared `Jeninnet.CQRS.Transport.Contracts` suite. They skip with
an inconclusive result when Docker is unavailable; CI runs them against a real broker and also
restarts the broker application to prove consumption resumes.
