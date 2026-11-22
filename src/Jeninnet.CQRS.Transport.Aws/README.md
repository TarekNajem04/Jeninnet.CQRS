# Jeninnet.CQRS.Transport.Aws

AWS transport for `Jeninnet.CQRS`: SQS queues for point-to-point sends and SNS topics with raw
SQS subscriptions for fan-out publishes. Credentials come from the standard AWS SDK chain.

```csharp
builder.Services.AddCqrsMessageSerialization();
builder.Services.AddSingleton<IMessageTransportHandler, OrderMessageHandler>();
builder.Services.AddCqrsAws(options => {
    options.Region = "eu-west-1";
    // options.ServiceUrl = "http://localhost:4566"; // LocalStack
    options.UseFifo = true; // optional; PartitionKey becomes MessageGroupId
    options.Subscriptions.Add(new AwsSubscription { MessageType = typeof(OrderCreated) });
});

await transport.PublishAsync(new OrderCreated(id), new TransportMessageOptions {
    PartitionKey = orderId,
});
```

`AddCqrsAws` selects `CqrsTransportMode.External`, registers the transport, the core in-memory
claim-check store (replace it with the EF store for durability), and a hosted service that owns
the clients. User registrations made before it win (`TryAdd`).

## Topology

- **Send** uses one SQS queue per message type; the first handler consumes it.
- **Publish** uses one SNS topic per message type. Each handler gets its own SQS subscriber queue
  (`{queue}.sub.{index}`) subscribed with `RawMessageDelivery=true` and a queue policy allowing
  SNS to send to it. This gives fan-out independent of SQS competing consumers.
- `UseFifo = true` suffixes queue/topic names with `.fifo`, enables FIFO attributes, and maps the
  envelope `PartitionKey` to `MessageGroupId` (message id to `MessageDeduplicationId`).
- Queue names are fitted to the 80-character SQS limit with a stable hash suffix; topic names
  respect the SNS limit.
- `EnsureTopology = false` expects pre-provisioned entities and resolves them by name.

## Delivery, visibility, and claim-check

SQS uses at-least-once delivery. The message is deleted only after the handler succeeds; failures
leave it for redelivery after the visibility timeout. While a handler runs, a heartbeat calls
`ChangeMessageVisibility` every `VisibilityRenewalInterval` (must be shorter than
`VisibilityTimeout`), so a handler longer than the timeout is not redelivered.

The SQS/SNS 256 KB cap is enforced through the existing claim-check seam. Payloads above the
configured claim-check threshold are staged in `IClaimCheckStore` and the broker body carries only
the reference; `AwsMessageCodec` throws a clear error if the final body is still over
`PayloadLimitBytes` instead of truncating or losing the message. A 300 KB payload test proves the
handoff end to end. The in-memory claim-check default is per-process; use the EF Core store for
restart-safe large messages.

## Options

| Option | Default | Purpose |
|---|---|---|
| `Region` | `us-east-1` | AWS region |
| `ServiceUrl` | `null` | LocalStack/VPC endpoint override |
| `UseFifo` | `false` | FIFO queues/topics and message groups |
| `EnsureTopology` | `true` | Create queues/topics/subscriptions |
| `VisibilityTimeout` | `30s` | SQS lease |
| `VisibilityRenewalInterval` | `10s` | Handler-time heartbeat |
| `ReceiveWaitSeconds` | `1` | SQS long poll |
| `MaxNumberOfMessages` | `10` | Batch receive |
| `PayloadLimitBytes` | `240 KiB` | Inline ceiling |

## Tests and frameworks

Targets `net10.0`/`net11.0`; the `netstandard2.0` asset arrives with `CQRS-53` (the AWS SDK
already supports it). Integration tests use LocalStack `4.14` with the standard credential chain
(environment test credentials), the shared contract suite, a 300 KB claim-check round trip, a
visibility-extension test with a one-second timeout and a three-second handler, and FIFO ordering.
Tests become inconclusive when Docker is unavailable; CI runs them against LocalStack.
