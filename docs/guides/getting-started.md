# Getting started

**Current preview:** `1.0.0-preview.1`. Install the pinned packages:

```powershell
dotnet add package Jeninnet.CQRS --version 1.0.0-preview.1
dotnet add package Jeninnet.CQRS.Generators --version 1.0.0-preview.1
```

```csharp
builder.Services.AddCqrsByReflection();
```

Register handlers (or use the source-generated registration), then dispatch through
`ICqrsBroker`:

```csharp
var result = await broker.DispatchAsync<PlaceOrder, OrderId>(command, cancellationToken);
```

Optional packages compose as registration calls:

| Need | Package | Registration |
|---|---|---|
| Validation before handlers | `Jeninnet.CQRS.Validation` | `AddCqrsValidationBehavior()` |
| Retry and circuit breaker | `Jeninnet.CQRS.Resilience` | `AddCqrsResilienceBehavior()` |
| Memory caching | `Jeninnet.CQRS.Caching` | `AddCqrsCachingBehavior()` |
| Distributed caching | `Jeninnet.CQRS.Caching` | `AddCqrsDistributedCaching()` |
| Deferred commands + dead letters | core | `AddCqrsDeferredDelivery()` |
| Broker-free test transport | core | `AddCqrsInMemoryTransport()` |
| RabbitMQ transport | `Jeninnet.CQRS.Transport.RabbitMq` | `AddCqrsRabbitMq(...)` |
| Kafka transport | `Jeninnet.CQRS.Transport.Kafka` | `AddCqrsKafka(...)` |
| Azure Service Bus transport | `Jeninnet.CQRS.Transport.AzureServiceBus` | `AddCqrsAzureServiceBus(...)` |
| AWS SQS/SNS transport | `Jeninnet.CQRS.Transport.Aws` | `AddCqrsAws(...)` |
| MQTT transport | `Jeninnet.CQRS.Transport.Mqtt` | `AddCqrsMqtt(...)` |
| HTTP transport (outbound) | `Jeninnet.CQRS.Transport.Http` | `AddCqrsHttpTransport(...)`, `HttpTransportWire.ReadEnvelope(...)` |
| SignalR transport | `Jeninnet.CQRS.Transport.SignalR` | `AddCqrsSignalR(...)` + `MapCqrsSignalRHub(...)` |
| Newtonsoft.Json wire format | `Jeninnet.CQRS.Serialization.NewtonsoftJson` | `AddCqrsNewtonsoftJsonSerialization(...)` |
| MessagePack wire format | `Jeninnet.CQRS.Serialization.MessagePack` | `AddCqrsMessagePackSerialization(...)` |
| Saga state machines (in-memory) | `Jeninnet.CQRS.Sagas` | `AddCqrsSagas<TSaga>(...)` |
| Durable saga state | `Jeninnet.CQRS.Sagas.EntityFrameworkCore` | `AddCqrsEfSagas<TSaga>(...)` |
| Stuck-saga health check | `Jeninnet.CQRS.HealthChecks` | `AddCqrsSagaHealthChecks(...)` |
| Durable EF Core stores | `Jeninnet.CQRS.Idempotency.EntityFrameworkCore` | `AddCqrsEfIdempotencyStore(...)` |
| Notification outbox | core / EF | `AddCqrsNotificationOutbox()` / `AddCqrsEfNotificationOutbox(...)` |
| Transport outbox relay | core | `AddCqrsOutboxRelay<TTransport>(...)` |
| Request/response over transport | core | `AddCqrsRequestResponse()` |
| Durable request inbox | EF | `AddCqrsEfRequestResponse(...)` |
| Persistent message scheduler | core / EF | `AddCqrsScheduler()` / `AddCqrsEfScheduler(...)` |
| Multi-tenancy (ambient context + tenant partitions) | core | `AddCqrsMultiTenancy<TResolver>()` |
| Scheduler due-backlog health check | `Jeninnet.CQRS.HealthChecks` | `AddCqrsSchedulerHealthChecks(...)` |
| Minimal-API endpoints | `Jeninnet.CQRS.AspNetCore` | `AddCqrsEndpoints()` + `MapCqrsCommand<...>()` |
| Consumer endpoint policy + filters | core | `ConfigureEndpoint<TMessage>(...)` + `AddReceiveFilter<TMessage, TFilter>()` |
| Consumer inbox (dedupe inbound message ids) | core / EF | `AddCqrsInbox(...)` / `AddCqrsEfInbox(...)` |
| Routing slips (linear activities + compensation) | Sagas | `AddRoutingSlip<TArguments>(...)` / `AddCqrsEfRoutingSlips(...)` |
| Background jobs (progress + polling) | core / EF | `AddCqrsJobs(...)` + `AddJobHandler<TJob, THandler>()` / `AddCqrsEfJobs(...)` |
| Message observers (send/publish/consume/fault) | core / Sagas | `AddSendObserver<T>()`, `AddPublishObserver<T>()`, `AddConsumeObserver<T>()`, `AddCqrsSagaObserver<T>()` |
| Saga test harness + repository seam | Testing / Sagas | `new SagaTestHarness<TState, TEvent>(machine)` + `ISagaRepository<TState>` |
| Claim-check providers (file/Azure Blob/S3) | ClaimCheck.* | `AddCqrsFileSystemClaimCheck(...)`, `AddCqrsAzureBlobClaimCheck(...)`, `AddCqrsAmazonS3ClaimCheck(...)` |
| Multi-transport riders | core | `AddCqrsRider("name", transport)` + `Route<TMessage>().To("name")` |
| Retention sweeper + archive tables | core / EF | `AddCqrsRetention(...)` + `AddCqrsEfRetention(...)` |
| Shared message contracts | `Jeninnet.CQRS.Contracts` | `ICommand<T>`, `IQuery<T>`, `INotification`, `IEvent`, `[MessageVersion]` |

Claim-check is opt-in through `AddCqrsClaimCheck()`; each provider extension replaces the
in-memory store with a durable one. Keep `ClaimCheckOptions.ThresholdBytes` below the smallest
transport payload cap (for example SQS's 256 KB) and above your median message. Providers store
bytes as-is: file volumes, Azure Storage encryption, and S3 SSE-KMS are deployment concerns; see
each provider README for sizing and encryption guidance.

### Retention and archives

Retention is opt-in and dry-run by default. Add a policy per store, then enable a destructive
sweep explicitly when the counts look right:

```csharp
builder.Services.AddCqrsEfRetention(configure: options => {
    options.Interval = TimeSpan.FromHours(1);
    options.DryRun = false;
    options.AddPolicy(RetentionStoreNames.Inbox, new RetentionPolicy { Retention = TimeSpan.FromDays(7) });
    options.AddPolicy(RetentionStoreNames.DeadLetters, new RetentionPolicy {
        Retention = TimeSpan.FromDays(30),
        GracePeriod = TimeSpan.FromDays(1),
    });
});
builder.Services.AddCqrsEfSagaRetention(configure: options =>
    options.AddPolicy(RetentionStoreNames.Sagas, new RetentionPolicy { Retention = TimeSpan.FromDays(30) }));
builder.Services.AddCqrsRetentionHealthChecks();
```

The EF adapters archive completed inbox/outbox rows, terminal jobs, dead letters after their
grace period, and terminal sagas; the archive tables keep the source schema plus `ArchivedAtUtc`.
A store with no policy is never touched. The in-memory sweep lease is single-host; replace
`IRetentionLease` with a durable implementation before running several sweepers.

### Multi-transport riders

A rider is a named transport plus a message-type routing table. The router becomes the registered
`IMessageTransport`, so the outbox relay and other senders use the same table:

```csharp
builder.Services.AddCqrsRider("commands", rabbitTransport);
builder.Services.AddCqrsRider("events", kafkaTransport);

builder.Services.Route<PlaceOrder>().To("commands");
builder.Services.Route<OrderPlaced>().To("events");

builder.Services.AddCqrsRiderHealthChecks();
builder.Services.AddCqrsOutboxRelay<CqrsRiderTransport>();
```

Every declared route must have a target and every configured receive endpoint must have a route;
violations abort startup with the offending message type. The rider host starts transports in
registration order and stops them in reverse order. Health data and the `Jeninnet.CQRS.Riders`
metrics carry the rider name and transport type. Cross-broker transactions are intentionally out
of scope: use an outbox and inbox per transport when a workflow spans brokers.

Transport consumers share one endpoint policy. Configure it once and keep broker-specific options
in the package that owns them:

```csharp
builder.Services.ConfigureEndpoint<OrderPlaced>(endpoint => endpoint
    .Named("orders")
    .WithPrefetch(32)
    .WithConcurrency(4)
    .WithRetry(3, TimeSpan.FromSeconds(1))
    .WithDeadLetterTarget("orders.dlq")
    .WithConsumptionTimeout(TimeSpan.FromSeconds(30))
    .WithFilter<AuditFilter>());

builder.Services.AddReceiveFilter<OrderPlaced, TenantFilter>(); // global for the message type
```

Filters run in registration order inside the shared receive pipeline, before the consumer handler;
a filter returning `false` drops the delivery. `EndpointConfigurationRegistry` exposes the
validated prefetch/concurrency/dead-letter policy to every transport adapter.

At-least-once brokers redeliver. Add the consumer inbox so a message id runs its handler once:

```csharp
builder.Services.AddCqrsInbox(); // in-memory; AddCqrsEfInbox(...) for a durable table
```

The inbox records the id before the handler and completes it after success; a failed handler
releases the claim for an immediate retry. The outbox and inbox are independent: the outbox
deduplicates publication, the inbox deduplicates consumption. A crash between handler success and
inbox completion is the documented at-least-once window; set `InboxOptions.AutoComplete = false`
and complete the row inside the handler's transaction to close it.

Portable packages also ship a `netstandard2.0` asset: core dispatch, Caching, Idempotency,
Resilience, Diagnostics, HealthChecks, Testing, Sagas, the claim-check providers, the broker
transports, and the MessagePack/Newtonsoft.Json formats. Core, Caching, Idempotency, Resilience,
Diagnostics, HealthChecks, Testing, and Sagas additionally ship a `net462` asset for legacy Windows
hosts;
`samples/Jeninnet.CQRS.Sample.NetFramework` is the dispatch smoke. Validation, the result
adapters, FluentValidation, EF Core, and the ASP.NET Core adapter remain framework-blocked; the
reasons and revisit triggers are in the [ADR registry](../adr/README.md) (see
[ADR-0006](../adr/ADR-0006-net462-dispatch-boundary.md) for the net462 boundary).

For a linear sequence with automatic undo, register a routing slip. Each activity has only
`ExecuteAsync` and `CompensateAsync`; progress is persisted after every step so a restart resumes
at the next operation:

```csharp
builder.Services.AddRoutingSlip<OrderArguments>(slip => slip
    .AddStep<ReserveStockActivity>("reserve")
    .AddStep<ChargeCardActivity>("charge")
    .AddStep<ShipOrderActivity>("ship"));

builder.Services.AddCqrsEfRoutingSlips(
    db => db.UseSqlite("Data Source=sagas.db")); // durable; in-memory is the default
```

A step failure compensates completed steps in reverse order; a failed compensation is dead-lettered
with both errors. Per-step retry/timeout and the progress/health APIs are described in the
`Jeninnet.CQRS.Sagas` README.

Long-running work can be submitted as a durable job with progress and a polling API:

```csharp
builder.Services.AddCqrsJobs(options => {
    options.MaxAttempts = 5;
    options.InitialRetryDelay = TimeSpan.FromSeconds(2);
});
builder.Services.AddJobHandler<GenerateReport, GenerateReportHandler>();
builder.Services.AddCqrsEfJobs(db => db.UseSqlite("Data Source=jobs.db")); // in-memory is the default

var jobId = await queue.SubmitAsync(new GenerateReport("2026-11"), cancellationToken);
var status = await client.GetStatusAsync(jobId, cancellationToken); // Queued/Running/Succeeded/Failed/Canceled
await queue.CancelAsync(jobId, cancellationToken);
```

`IJobHandler<TJob>` receives an `IJobProgress` reporter. The worker leases a single owner, renews
the lease while the handler runs, retries with exponential backoff, and writes exhausted jobs to
the dead-letter store. Terminal jobs are pruned after `JobOptions.Retention`.

Cross-cutting observation is separate from handler logic. Send, publish, consume, and saga
observers see the canonical envelope and run in registration order:

```csharp
builder.Services.AddSendObserver<AuditSendObserver>();
builder.Services.AddConsumeObserver<MetricsConsumeObserver>();
builder.Services.AddCqrsSagaObserver<AuditSagaObserver>();

public sealed class AuditSendObserver : ISendObserver {
    public Task OnSendingAsync(OutboundObservationContext context, CancellationToken cancellationToken) { ... }
    public Task OnSentAsync(OutboundObservationContext context, CancellationToken cancellationToken) { ... }
    public Task OnSendFaultAsync(OutboundObservationContext context, Exception error, CancellationToken cancellationToken) { ... }
}
```

An observer that throws is logged and counted (`cqrs.observer.faults`), never breaking messaging.
With no observer registered the invocation helper returns immediately without allocating a
context. Register an observer instance directly (`AddSingleton<ISendObserver>(instance)`) for a
global hook, or use the typed add methods for the built-in registration path.

The nightly quality gate runs Stryker.NET over the dispatch, resilience, idempotency, testing,
and saga surfaces. `scripts/mutation/run-all.ps1` enforces the per-config floors and writes a
dashboard with survivors grouped by mutator; the baseline and exclusions live in
`tests/Mutation/README.md`.

See the [repository README](https://github.com/TarekNajem04/Jeninnet.CQRS#readme) for the
end-to-end guide, and the [API reference](xref:Jeninnet.CQRS) for every public type.