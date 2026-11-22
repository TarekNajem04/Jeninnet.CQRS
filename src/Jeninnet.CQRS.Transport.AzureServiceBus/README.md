# Jeninnet.CQRS.Transport.AzureServiceBus

Azure Service Bus transport for `Jeninnet.CQRS`: queues for point-to-point sends,
topic/subscriptions for publish fan-out, optional sessions for ordered handling, native scheduled
enqueue, and dead-letter forwarding into the CQRS dead-letter store.

```csharp
builder.Services.AddCqrsMessageSerialization();
builder.Services.AddSingleton<IMessageTransportHandler, OrderMessageHandler>();
builder.Services.AddCqrsAzureServiceBus(options => {
    options.ConnectionString = builder.Configuration.GetConnectionString("ServiceBus");
    // or: options.FullyQualifiedNamespace = "my-ns.servicebus.windows.net";
    //     options.Credential = new DefaultAzureCredential();
    options.Subscriptions.Add(new AzureServiceBusSubscription { MessageType = typeof(OrderCreated) });
    options.SessionsEnabled = true; // optional; PartitionKey becomes SessionId
});

await transport.PublishAsync(new OrderCreated(id), new TransportMessageOptions {
    PartitionKey = orderId,
    ScheduledEnqueueTimeUtc = DateTimeOffset.UtcNow.AddMinutes(5), // native ASB scheduling
});
```

`AddCqrsAzureServiceBus` selects `CqrsTransportMode.External`, registers the transport, the core
dead-letter store, and a hosted service that owns the SDK client lifetime.

## Topology and auth

- **Send** goes to the message type's queue (`TransportNamingConvention.Queue`).
- **Publish** goes to the type's topic; each handler gets a subscription named
  `{SubscriptionPrefix}` (first handler) or `{SubscriptionPrefix}.{index}`. This makes publish fan
  out while send stays point-to-point.
- `EnsureTopology = true` (default) creates queues, topics, and subscriptions through
  `ServiceBusAdministrationClient`. Set it to `false` when infrastructure is provisioned
  separately.
- Auth is either a connection string (including the local emulator) or
  `FullyQualifiedNamespace` + a `TokenCredential` for managed identity. Configure exactly one.
- `SessionsEnabled = true` marks every created queue/subscription session-enabled; the envelope
  `PartitionKey` becomes the Service Bus `SessionId`, and one session is processed at a time to
  preserve per-session ordering. Without sessions a single receiver processes sequentially.

## Delivery and dead letters

Receivers use PeekLock and `CompleteMessageAsync` only after the handler succeeds. On failure the
transport records a copy (original headers and payload) in `IDeadLetterStore` and calls
`DeadLetterMessageAsync` on the broker, so the message is inspectable in both places. Delivery is
at-least-once; duplicate detection is **not** relied on because idempotency is owned by CQRS.
Scheduled messages use native `ScheduledEnqueueTime` and do not occupy a receiver until due.

## Options

| Option | Default | Purpose |
|---|---|---|
| `ConnectionString` / `FullyQualifiedNamespace` + `Credential` | — | Auth (exactly one) |
| `EnsureTopology` | `true` | Create queues/topics/subscriptions |
| `SubscriptionPrefix` | `cqrs` | Publish subscription names |
| `SessionsEnabled` | `false` | Ordered session handling |
| `PrefetchCount` | `0` | Receiver prefetch |
| `ReceiveWaitTime` | `1s` | Poll wait per receive |
| `LockDuration` | `5m` | Entity lock duration |

## Tests and frameworks

Targets `net10.0`/`net11.0`; the `netstandard2.0` asset arrives with `CQRS-53`. Integration tests
use the Testcontainers Azure Service Bus emulator (and its MSSQL sidecar), the shared
`Jeninnet.CQRS.Transport.Contracts` suite, plus session-ordering, scheduled-delivery, and
dead-letter-header tests. Tests become inconclusive when Docker is unavailable; CI runs them
against the emulator.
