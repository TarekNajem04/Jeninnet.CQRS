# Jeninnet.CQRS.Transport.Kafka

Kafka transport for `Jeninnet.CQRS`: topic-per-message-type, keyed partitioning, consumer groups,
and manual offset commits after handler success.

```csharp
builder.Services.AddCqrsMessageSerialization();
builder.Services.AddSingleton<IMessageTransportHandler, OrderMessageHandler>();
builder.Services.AddCqrsKafka(options => {
    options.BootstrapServers = "kafka:9092";
    options.ConsumerGroup = "orders";
    options.Subscriptions.Add(new KafkaSubscription { MessageType = typeof(OrderCreated) });
});

await transport.PublishAsync(new OrderCreated(id), new TransportMessageOptions {
    PartitionKey = orderId, // same key => same partition => ordered
});
```

`AddCqrsKafka` selects `CqrsTransportMode.External`, registers the transport, and adds a hosted
service that owns producer/consumer lifetime. User registrations made before it win (`TryAdd`).

## Topics and partitioning

The topic name comes from `TransportNamingConvention` (`cqrs.{full-name}`) and can be overridden
per subscription. Topics are created on start when missing (`EnsureTopics`, partitions
`NumPartitions`, replication factor). The producer key is `PartitionKey`, falling back to the
tenant id and then the message id; a stable key keeps one aggregate's messages on one partition,
and one consumer per group processes them in order. Consumer groups are named
`{ConsumerGroup}` for one handler and `{ConsumerGroup}.{index}` for additional handlers, so
`Publish` fans out and `Send` is handled by the first group only.

## Delivery semantics

`EnableAutoCommit` and `EnableAutoOffsetStore` are off. The consumer commits only after the
handler returns; a failed handler seeks back to the same offset and retries after
`HandlerFailureDelay`, leaving the offset uncommitted so a restart also redelivers it. This is
at-least-once. **Exactly-once transactions and Kafka Streams are out of scope** (revisit when a
transactional outbox/handler resource can join the Kafka transaction). Producer idempotence is on
by default to avoid duplicate retries per partition, not to promise exactly-once handler effects.

## Options

| Option | Default | Purpose |
|---|---|---|
| `BootstrapServers` | `localhost:9092` | Broker endpoint |
| `ConsumerGroup` | `cqrs` | Base consumer-group id |
| `ClientId` | `null` | Broker-visible client id |
| `EnsureTopics` | `true` | Create missing topics on start |
| `NumPartitions` | `3` | Topic partition count |
| `ReplicationFactor` | `1` | Topic replication factor |
| `EnableIdempotence` | `true` | Producer idempotence |
| `HandlerFailureDelay` | `500ms` | Retry pacing after a handler failure |

## Tests and frameworks

The package targets `net10.0`/`net11.0` because core does; the `netstandard2.0` asset arrives with
`CQRS-53` (Confluent.Kafka already supports it). Integration tests use Testcontainers with
`confluentinc/cp-kafka` and the shared `Jeninnet.CQRS.Transport.Contracts` suite, including
manual-commit/redelivery and per-key ordering tests. Tests become inconclusive when Docker is
unavailable; CI runs them against a real broker.
