# Changelog

All notable changes to the Jeninnet.CQRS solution are documented here.

The format is inspired by Keep a Changelog. `1.0.0-preview.1` is the first public preview release
and is not a stable release declaration. Changes after this preview are tracked under
`[Unreleased]` until they ship.

---

## Versions

| Version | Status |
|---|---|
| `1.0.0-preview.1` | Current preview, prepared 2026-11-22 (first public release) |
| `1.0.0` | Historical pre-parity baseline (2026-11-18; never published) |

---

## [1.0.0-preview.1] - 2026-11-22

**Status:** Current preview. First public release of the 78/78 parity tree: 31 packable
packages, 68/68 tasks `DONE`, full build/test/docs/AOT/pack gates green.

### Versioning and release identity

- Centralized the product version in `Directory.Build.props` as `1.0.0-preview.1`, matching the
  sibling Jeninnet projects. `AssemblyVersion`/`FileVersion` stay numeric at `1.0.0.0`, and
  `PackageVersion` follows `$(Version)`; package projects do not declare their own version.
- Added the SemVer release policy and status-at-a-glance table to `ROADMAP.md`, and refreshed the
  release summary with accomplished and remaining work.
- Final publish audit: package metadata now carries the repository/project URL and MIT license
  expression, analyzer/generator packages no longer leak compiler dependencies, and the
  intentional `netstandard2.0` polyfill surface is documented in
  `src/Jeninnet.CQRS/CompatibilitySuppressions.xml` for package validation.

### Parity audit and ADR registry (`CQRS-68`)

- Added a machine-checked parity process: `scripts/parity/extract-matrix.ps1` verifies that every
  non-`●` row in the 78-row matrix has a task, with self-test fixtures and an artificial-gap
  failure proof. The full matrix and ledger stay in the maintainer workspace because they contain
  internal peer analysis; the checker and its self-test are public.
- Added a quarterly parity audit checklist (maintainer workspace), [ADR template](docs/adr/TEMPLATE.md),
  and the [ADR registry](docs/adr/README.md).
- Regenerated the matrix to **78/78 `●`**, recomputed support shares, marked the older 37-row
  snapshot superseded, and updated the roadmap to completed. Package count remains 31.
  Progress: `CQRS-68`.

### MQTT transport (`CQRS-67`)

- `Jeninnet.CQRS.Transport.Mqtt` (`netstandard2.0`/`net10.0`/`net11.0`) adds topic-per-message-type
  routing with an optional tenant level, one `MqttTopicConvention` formatter, QoS 0/1/2 mapping,
  retained messages, clean-session control, and an owned reconnect loop that resubscribes after a
  broker restart. Envelope metadata travels as MQTT 5 user properties.
- The adapter acknowledges only after local handlers succeed, documents that QoS 2 is a
  broker-level guarantee rather than transactional exactly-once handler execution, and passes the
  shared transport contract suite against both Mosquitto (Testcontainers) and an in-process MQTTnet
  broker. `ADR-0011` records the MQTTnet 4.3 dependency for `netstandard2.0` reach.
- Package count is now 31. Progress: `CQRS-67`.

### HTTP and SignalR transports (`CQRS-66`)

- `Jeninnet.CQRS.Transport.Http` posts the canonical envelope to a remote endpoint with API-key
  and HMAC-SHA256 options, a documented wire contract (`HttpTransportWire`), and no internal
  retry; the outbox relay stays the single retry owner. It targets
  `netstandard2.0`/`net10.0`/`net11.0`.
- `Jeninnet.CQRS.Transport.SignalR` adds a reconnecting client transport and a stateless relay
  hub (`MapCqrsSignalRHub`). Publish fans out to connected clients; send returns to the calling
  connection. Both pass the shared transport contract suite, including a real hub-restart
  reconnect test. It targets `net10.0`/`net11.0` because the relay hub uses the ASP.NET Core
  shared framework.
- The WebApi sample selects either transport through `Cqrs:Transport` and always maps the relay
  hub. Package count is now 30. Progress: `CQRS-66`.

### Zero-dependency message contracts package (`CQRS-65`)

- `Jeninnet.CQRS.Contracts` (`netstandard2.0`, no external packages) now owns `ICommand`,
  `ICommand<T>`, `IQuery<T>`, `IStreamQuery<T>`, `INotification`, `IEvent`, `Unit`,
  `[MessageVersion]`, and `MessageHeaders`.
- `Jeninnet.CQRS` references the contracts package and forwards every moved type, so existing
  source and compiled consumers keep the `Jeninnet.CQRS.Interfaces.*` / `Jeninnet.CQRS.Unit`
  identity without a namespace change.
- Handler and dispatcher interfaces remain in core; `ADR-0009` records why behavioral contracts
  stay with the runtime. Package count is now 28. Progress: `CQRS-65`.

### Retention sweeper and archive tables (`CQRS-64`)

- `AddCqrsRetention` adds the policy-driven hosted sweeper with interval, batch size, dry-run
  default, lease, `cqrs.retention.archived`/`deleted` counters, and `AddCqrsRetentionHealthChecks`.
- `AddCqrsEfRetention` archives completed inbox/outbox rows, terminal jobs, and dead letters after
  their configured grace period; `AddCqrsEfSagaRetention` archives terminal sagas. Archive tables
  keep the source schema plus `ArchivedAtUtc`; stores without a policy are untouched.
- Dry runs report the same eligible counts without changing storage; completed outbox rows keep a
  completion timestamp until retention archives them. Cold-storage CSV/Parquet export is deferred
  in `ADR-0008`. Progress: `CQRS-64`.

### Multi-transport riders (`CQRS-63`)

- `AddCqrsRider(name, transport)` registers named transports; `Route<TMessage>().To("name")`
  builds the message-type routing table, and `CqrsRiderTransport` becomes the primary
  `IMessageTransport` for the outbox relay and other senders.
- Startup validation rejects incomplete routes, unknown rider names, and configured receive
  endpoints without a route, naming every offending message type.
- `CqrsRiderHost` starts riders in registration order and stops them in reverse; health data and
  the `Jeninnet.CQRS.Riders` counters carry the rider and transport tags.
- `AddCqrsRiderHealthChecks()` reports one `rider.{name}` entry per rider. Cross-broker
  transactions remain out of scope: use an outbox and inbox per transport. Progress: `CQRS-63`.

### Claim-check providers for file, Azure Blob, and S3 (`CQRS-62`)

- `Jeninnet.CQRS.ClaimCheck.FileSystem`, `.AzureBlob`, and `.AmazonS3` implement the existing
  `IClaimCheckStore` seam with content-addressed SHA-256 keys; storing the same payload twice keeps
  one entry.
- `IClaimCheckStoreMaintenance` adds `CountAsync` and a reference-aware, age-based
  `CleanupAsync`; `CleanupExpiredAsync(TimeSpan, referencedKeys)` supplies the TTL cutoff.
- Shared provider contract tests run the same put/get/delete, dedupe, and TTL scenarios against a
  temp directory, Azurite, and LocalStack. Cloud suites become inconclusive when Docker is absent.
- Provider READMEs document threshold sizing and encryption as deployment concerns (volume
  encryption, Azure CMK, S3 SSE-KMS); `ADR-0007` records the GCS deferral and revisit trigger.
  Package count is now 27. Progress: `CQRS-62`.

### Saga test harness and pluggable repositories (`CQRS-61`)

- `ISagaRepository<TState>` and `SagaRepository<TState>` provide a strongly typed seam over any
  `ISagaStore`; the in-memory and EF Core stores satisfy the same save/load/delete and
  stale-version contract.
- `SagaTestHarness<TState, TEvent>` from `Jeninnet.CQRS.Testing` builds the real dispatcher around
  an in-memory store (or a supplied EF store), records every follow-up command and notification,
  and returns the typed state after each event. No host, handlers, or container setup are needed.
- `ShouldHaveTransitionedTo` / `ShouldHavePublished` / `ShouldNotHavePublished` assertion helpers
  sit on top of `Jeninnet.Assertions`; the no-op recording broker never executes follow-ups.
- A shared repository contract project runs the same scenarios against the in-memory and EF
  stores. New tests use `Jeninnet.Assertions`. Progress: `CQRS-61`.

### Message observers and lifecycle hooks (`CQRS-60`)

- `ISendObserver`, `IPublishObserver`, and `IConsumeObserver` expose pre/post/fault hooks for the
  transport lifecycle; `ISagaObserver` exposes the same shape for saga dispatches. Observers
  receive the canonical `TransportEnvelope`/context, not broker types, and run in registration
  order.
- `AddSendObserver<T>()`, `AddPublishObserver<T>()`, `AddConsumeObserver<T>()`, and
  `AddCqrsSagaObserver<T>()` register observers by type; a directly registered singleton works as
  a global hook. Exceptions are isolated, logged, and counted as `cqrs.observer.faults`; they
  never break the send, publish, consume, or saga transition.
- The in-memory and broker transports call the shared observer invoker; the receive pipeline adds
  consume pre/post/fault around filters, middleware, and the handler. The no-observer path returns
  `Task.CompletedTask` without allocating a context or state machine.
- New tests use `Jeninnet.Assertions`. Progress: `CQRS-60`.

### Distributed job consumers (`CQRS-59`)

- `IJobQueue.SubmitAsync<TJob>(job)` creates a durable job; `IJobClient.GetStatusAsync(jobId)`
  reports `Queued`/`Running`/`Succeeded`/`Failed`/`Canceled`, progress 0–100, attempts, and the
  last error. `AddJobHandler<TJob, THandler>()` closes the handler type at registration.
- `JobWorker` leases due rows with a single owner, renews the lease while the handler runs,
  retries failures with the configured exponential backoff, and records exhausted jobs in the
  dead-letter store with the terminal error. A queued cancellation is terminal immediately; a
  running cancellation is observed by a watcher and the handler's token.
- `IJobStore` ships an in-memory store and `AddCqrsEfJobs` adds the durable `Jobs` table to
  `IdempotencyDbContext` (beside the scheduler table, following its lease/backoff/retention
  conventions). `JobOptions` controls poll, batch, lease, cancellation poll, attempts, backoff,
  retention, and the pruner.
- Metrics `cqrs.job.active|submitted|started|succeeded|failed|canceled|retried|dead_lettered`.
  New tests use `Jeninnet.Assertions`. Progress: `CQRS-59`.

### Routing-slip orchestration (`CQRS-58`)

- `AddRoutingSlip<TArguments>(...)` builds a strongly typed `RoutingSlipDefinition` from ordered
  `IActivity<TArguments>` steps. Activities have exactly `ExecuteAsync` and `CompensateAsync`; the
  builder captures per-step retry attempts, retry delay, and timeout for both operations.
- The slip executor persists arguments and progress after every step and compensation. A step
  failure compensates completed steps in reverse order; a failed compensation records one dead
  letter with both errors through `RoutingSlipDeadLetter`. Cancellation leaves the slip `Running`
  for a restart; `ResumeAsync` continues at the next step or compensation.
- `IRoutingSlipStore` ships an in-memory store; `AddCqrsEfRoutingSlips` adds the durable
  `RoutingSlips` table to `SagaDbContext`. Progress inspection is `ListAsync`/`Get` snapshots.
- Metrics `cqrs.routing_slip.active|completed|compensated|faulted|step`; health contribution
  `AddCqrsRoutingSlipHealthChecks()` reports stuck running/compensating slips.
- New tests use `Jeninnet.Assertions`. Progress: `CQRS-58`.

### Consumer inbox (`CQRS-57`)

- `IInboxStore` with an in-memory default and an EF Core `Inbox` table deduplicates inbound
  transport messages by broker `MessageId`. The first delivery runs the handler; a redelivery with
  the same id is acknowledged without invoking it. A failed handler abandons the pending row so
  the broker can retry immediately.
- `InboxReceiveMiddleware` runs through the shared receive pipeline for every transport adapter.
  Pending rows expire after `InboxOptions.PendingTimeout`; the documented at-least-once window
  after a crash is closed by setting `AutoComplete = false` and completing the row inside the
  handler's transaction. `Retention` and the background pruner bound the table.
- Metrics: `cqrs.inbox.duplicates`, `cqrs.inbox.processed`, and `cqrs.inbox.pruned`. The outbox
  deduplicates publication and the inbox deduplicates consumption; they remain independent tables.
- `AddCqrsInbox(...)` wires the in-memory store; `AddCqrsEfInbox(...)` replaces it with the durable
  EF Core store and shares `IdempotencyDbContext`. New tests use `Jeninnet.Assertions`.
  Progress: `CQRS-57`.

### Consumer middleware and endpoint topology (`CQRS-56`)

- `ConfigureEndpoint<TMessage>(...)` builds an immutable `EndpointConfiguration` with name,
  prefetch, concurrent limit, retry policy, dead-letter target, and consumption timeout. The policy
  is validated once at host start; invalid values and filters scoped to an unknown endpoint abort
  startup with the endpoint name in the exception.
- `IReceiveFilter<TMessage>` filters run in registration order before the consumer handler, see the
  decoded message plus envelope headers, and can drop a delivery without invoking the handler.
  Filters are global for a message type or scoped to one endpoint through
  `AddReceiveFilter<TMessage, TFilter>("name")` or `WithFilter<TFilter>()`.
- `ReceiveFilterPipeline` is the shared consume seam: it applies filters, retry, and the
  consumption timeout around `IMessageTransportHandler`. The in-memory, RabbitMQ, Kafka, Azure
  Service Bus, and AWS SQS/SNS transports all route inbound deliveries through it. Prefetch,
  concurrency, and dead-letter values are exposed through `EndpointConfigurationRegistry` for
  broker adapters; a fake adapter test asserts the values arrive.
- The new tests use `Jeninnet.Assertions`. Progress: `CQRS-56`.

### Mutation testing gate (`CQRS-55`)

- Stryker.NET 5.1.0 is a local tool. Five configs mutate the dispatch core, the resilience
  behavior wrappers, the idempotency behavior/store, the testing kit, and the saga dispatcher.
  `ConfigureAwait`, `Guard.*`, and message-only string mutations are excluded with written
  reasons; no generated or analyzer code is mutated.
- `scripts/mutation/run-all.ps1` runs every config, enforces its `thresholds.break` floor, and
  writes `artifacts/mutation/dashboard.md`/`.json` with survivors grouped by mutator.
  `scripts/mutation/summarize.ps1` uses Stryker's score definition (timeouts count as detected).
- Nightly CI (03:00 UTC, plus manual dispatch) runs the gate on Windows and uploads the dashboard
  as an artifact. Per-PR mutation runs remain out of scope.
- Measured baseline/floors: core 100/100, resilience 100/100, idempotency 95.08/95,
  testing 100/100, sagas 40.23/35. Sagas is explicitly the weakest surface; its survivors are the
  documented next test wave, not silent exclusions. The first gate covers the highest-value
  public behavior files so the nightly run stays around nine minutes.
- Mutation work added characterization tests for dispatch/batch/streaming/ordering, the
  idempotency store boundary cases, the testing recorder/assertions, and saga resolve/correlation.
  It also exposed and fixed a real reflection-registration bug: a closed-generic
  `IPipelineBehavior` found by assembly scanning was registered as an open generic.
  Progress: `CQRS-55`.

### net462 dispatch reach (`CQRS-54`)

- Core, Caching, Idempotency, Resilience, Diagnostics, HealthChecks, Testing, and Sagas now ship
  a `net462` asset in addition to their portable targets. The same polyfill layer supplies
  `TimeProvider`, `Lock`, `PriorityQueue`, compiler attributes, and the guard/string/crypto
  helpers on .NET Framework.
- `samples/Jeninnet.CQRS.Sample.NetFramework` builds and runs one command and one query on
  .NET Framework 4.6.2; CI runs it on the Windows matrix.
- The legacy surface is supported but frozen: no net462-only APIs. EF Core stores, Validation,
  the result adapters, FluentValidation, ASP.NET Core, transports, and wire formats stay outside
  it (ADR-0006, with ADR-0001..0005 covering their dependency floors). The stale
  “Hosting 11 does not support net462” assumption was re-checked: `Microsoft.Extensions.Hosting`
  and its abstractions ship net462 assets, so the hosted worker types compile on the legacy leg.
- NuGet packages carry `lib/net462` for the eight dispatch/observability packages; the package
  scan and the smoke console are the evidence. Progress: `CQRS-54`.

### netstandard2.0 reach (`Jeninnet.CQRS.Polyfills` compatibility layer)

- Core, Caching, Idempotency, Resilience, Diagnostics, HealthChecks, Testing, Sagas, the four
  broker transports, and the MessagePack/Newtonsoft.Json formats now ship
  `netstandard2.0` in addition to `net10.0`/`net11.0`.
- A source-linked polyfill layer supplies `IsExternalInit`, `CallerArgumentExpression`,
  `RequiredMember`, nullable annotations, `Lock`, `PriorityQueue`, `TimeProvider`/`ITimer`,
  `Task.WaitAsync`, span/string/crypto/encoding helpers, and guard clauses. It is internal to
  each package; no extra assembly is shipped.
- The public `System.TimeProvider` shim exists only in the core `netstandard2.0` asset. Modern
  target frameworks use the framework type, and every package shares the same type identity.
- `ISagaStore` and `IDeferredEnvelope` no longer rely on default interface members; convenience
  overloads moved to extension methods and every envelope implements its members.
- A `net10.0` contract test project consumes all seven `netstandard2.0` assets via
  `SetTargetFramework` and runs dispatch plus a caching pipeline on them.
- Blocked packages are documented with dated ADRs: Validation (`ADR-0001`), the three result
  adapters (`ADR-0002`), FluentValidation (`ADR-0003`), EF Core stores (`ADR-0004`), and the
  ASP.NET Core adapter (`ADR-0005`). Progress: `CQRS-53`.

### Serialization formats and content negotiation (`Jeninnet.CQRS.Serialization.*`)

- New optional packages `Jeninnet.CQRS.Serialization.NewtonsoftJson` and
  `Jeninnet.CQRS.Serialization.MessagePack` plug into the existing `IMessageSerializer` seam.
  Core stays free of both libraries; the default `System.Text.Json` serializer still writes
  durable rows and outgoing messages.
- `MessageSerializerRegistry` negotiates `application/json`, `application/x-newtonsoft-json`,
  and `application/x-msgpack` by envelope content type, ignoring parameters such as
  `; charset=utf-8` and falling back to the default for legacy payloads without a content type.
  An unknown content type throws `UnknownContentTypeException` with the offending value and the
  registered formats, so relays dead-letter poison messages instead of misparsing them.
  `TransportMessageReader` is the receive-side entry point and uses the same registry.
  `TransportEnvelopeDeadLetter` keeps an unreadable envelope inspectable in `IDeadLetterStore`;
  the EF store rebuilds it as the same non-replayable poison entry instead of silently
  dispatching the envelope as a command.
- `TransportEnvelope` now carries `MessageVersion` (default 1); every transport writes
  `message-type`, `message-version`, and `content-type` headers and reads them back, with the
  legacy `x-message-type` header accepted for compatibility. The shared transport contract suite
  verifies the metadata across RabbitMQ, Kafka, Azure Service Bus, AWS SQS/SNS, and the in-memory
  transport.
- Newtonsoft uses the same camelCase web contract as `System.Text.Json`; MessagePack defaults to
  the standard resolver with source-generated formatters for AOT. A compatibility test round-trips
  the default JSON payload through a MassTransit-shaped envelope. Progress: `CQRS-46`.

### Transport outbox relay (`Jeninnet.CQRS` outbox)

- New `OutboxRelayWorker<TTransport>` hosted service publishes staged notifications through the
  registered `IMessageTransport` instead of the in-process relay. Register it with
  `AddCqrsOutboxRelay<TTransport>()` after the transport; the scoped publisher then stages only,
  and the legacy `OutboxDispatcher` stands down so a row is never published twice.
- `IOutboxStore` now exposes persisted leases (`LeaseAsync`, `CompleteLeaseAsync`,
  `FailLeaseAsync`, `ReleaseLeaseAsync`). In-memory and EF Core stores share the semantics:
  one owner per row, expired-lease reclamation, attempt counts, `NextAttemptUtc` backoff, and
  strict staged-order leasing where a leased or backing-off head blocks later rows.
- Failures use exponential backoff and move to `IDeadLetterStore` at `MaxAttempts` with the last
  error. Metrics `cqrs.outbox.published`, `cqrs.outbox.failed`, and `cqrs.outbox.lag_seconds`
  carry transport/message tags; the existing `outbox` health check measures the same store.
  EF Core adds nullable lease/backoff columns and a `Version` guard; migration SQL is in the
  package README. Progress: `CQRS-47`.

### Saga state machines (`Jeninnet.CQRS.Sagas`, `Jeninnet.CQRS.Sagas.EntityFrameworkCore`)

- New core package with `ISaga`, `ISagaState`, `SagaStateMachine<TState,TEvent>`, the fluent
  `Correlate`/`Initially`/`InState`/`Then`/`TransitionTo`/`Publish`/`PublishNotification`/
  `Schedule` table, `SagaDispatcher`, and in-memory `ISagaStore`. Events arrive through the new
  `INotificationObserver` seam, so sagas subscribe to ordinary notifications without reflection or
  per-event handler registrations.
- Unknown correlation ids create a new saga only for an `Initially()` transition; a non-initial
  event or an out-of-order event is ignored without writing. Follow-up commands are dispatched
  after the state save, and `SagaOptions.MaxConcurrencyRetries` reloads on a version conflict.
- New `Jeninnet.CQRS.Sagas.EntityFrameworkCore` package persists one composite-key row per state
  type + saga id with serialized state and a `Version` concurrency token, mirroring the
  idempotency store. RESTART tests run against SQLite. Targets: core `net10.0`/`net11.0`, EF
  `net11.0`. Progress: `CQRS-48`.

### Saga timeouts, compensation, and observability (`Jeninnet.CQRS.Sagas`)

- `OnTimeout`/`Schedule` persist a `SagaTimeoutCommand` through the existing deferred scheduler;
  `SagaTimeoutCommandHandler` resolves the event type from the configured machines and feeds it
  back into the transition table. A saga that already left the waiting state ignores the late
  timeout event.
- `Compensate` declares stable undo commands per transition. Successful steps are recorded in
  `SagaProgress`; a `Failure()` transition runs completed compensations in reverse, persisting
  progress after each command, so a restart resumes without repeating a completed undo.
- `Completed()`/`Failure()` mark terminal states. `SagaDiagnosticNames` exposes
  `cqrs.saga.active`, `cqrs.saga.completed`, `cqrs.saga.faulted`, and a `SagaStep` activity per
  event. `AddCqrsSagaHealthChecks()` reports active sagas older than
  `SagaHealthOptions.StuckAfter` as unhealthy.
- EF Core adds `CurrentState` and JSON `Progress` columns; migration SQL is in the package README.
  Progress: `CQRS-49`.

### Request/response over transports (`Jeninnet.CQRS` request/response)

- New `IRequestClient<TRequest,TResponse>` (`GetResponseAsync`, `GetAllResponsesAsync`),
  `IRequestResponder`/`RequestResponder<TRequest,TResponse>`, a transport handler, and
  `AddCqrsRequestResponse()`. Requests and responses are published envelopes carrying
  `x-cqrs-request-kind`/`x-cqrs-request-id`; the request id is also the correlation id.
- A response inbox tracks pending requests with a TTL. Timeouts throw
  `RequestTimeoutException` and remove the row; first-response clients complete on the first
  correlated reply, all-response clients aggregate until the timeout. The EF Core package adds
  `AddCqrsEfRequestResponse` and a `RequestInbox` table.
- New tests cover in-memory round-trips, timeout cleanup, first/all with two responders, and the
  durable inbox TTL. Progress: `CQRS-50`.

### Persistent message scheduler (`Jeninnet.CQRS` scheduling)

- `IMessageScheduler`/`MessageScheduler` provide delayed, absolute one-shot, and recurring
  five-field cron schedules over an `ISchedulerStore`. Payloads are serialized with the message
  serializer; `AddCqrsScheduler()` registers the in-memory store, hosted worker, and metrics.
- `SchedulerWorker` leases due rows with a version-guarded owner, publishes through
  `IMessageTransport`, deletes one-shot rows, and re-arms recurring rows from the cron expression.
  Failed publishes use exponential backoff; two instances cannot claim the same row. `CancelAsync`
  removes future rows without aborting an already-leased execution.
- EF Core adds `ScheduledMessages`/`EfSchedulerStore` and `AddCqrsEfScheduler(...)` with
  `NextRunTicks`/`LeaseExpiresTicks` indexes for portable due scans. Metrics
  `cqrs.scheduler.due`/`cqrs.scheduler.fired`/`cqrs.scheduler.failed` and
  `AddCqrsSchedulerHealthChecks()` expose backlog. Progress: `CQRS-51`.

### Multi-tenancy (`Jeninnet.CQRS` tenancy)

- `AddCqrsMultiTenancy<TResolver>()` (or a delegate resolver) registers an async-local
  `ITenantContext`, `ITenantResolver`, a strict `MultiTenancyOptions` gate, and one
  `ITenantScopedKey` composer. A missing tenant throws `TenantRequiredException` by default;
  `RequireTenant = false` enables optional tenancy.
- `CachingBehavior`, `DistributedCachingBehavior`, `IdempotencyBehavior`, `RateLimitBehavior`,
  and `PartitionedConcurrencyBehavior` prefix their cache/idempotency/rate-limit/concurrency
  keys with the tenant, so two tenants using the same caller key stay isolated.
- Transport envelopes carry the tenant (`x-tenant-id`) from the ambient context through the
  request client, in-memory transport, outbox relay, scheduler worker, and request/response
  responder. Deferred, recurring, outbox, dead-letter, request, and scheduler envelopes persist
  the tenant and restore its scope around execution.
- Every EF message table gains a `TenantId` column and a global query filter; `Records`,
  `DeferredCommands`, and `ScheduledMessages` use `(TenantId, Key)` composite keys so callers in
  two tenants can reuse the same idempotency/schedule key. Progress: `CQRS-52`.

### RabbitMQ transport (`Jeninnet.CQRS.Transport.RabbitMq`)

- New transport package on RabbitMQ.Client 7: fan-out exchange per message type, durable queues
  per consumer endpoint, direct dead-letter exchange/queue, publisher confirms by default,
  prefetch and consumer concurrency, and an owned exponential-backoff reconnect loop. The shared
  transport contract suite passes against a Testcontainers RabbitMQ broker, including
  broker-restart consumption recovery and handler-failure dead-lettering. The package targets
  `net10.0` and `net11.0`; its `netstandard2.0` asset joins when core gains that target
  (`CQRS-53`).

### Kafka transport (`Jeninnet.CQRS.Transport.Kafka`)

- New transport package on Confluent.Kafka 2.16: topic-per-message-type, explicit topic creation,
  keyed partitioning for per-aggregate ordering, one consumer group per handler, manual commits
  only after handler success, manual-commit redelivery, producer idempotence, and the shared
  transport contract suite against a Testcontainers Kafka broker. At-least-once is documented;
  exactly-once transactions and Kafka Streams are explicitly out of scope. Targets `net10.0` and
  `net11.0`; the `netstandard2.0` asset joins with `CQRS-53`.

### Azure Service Bus transport (`Jeninnet.CQRS.Transport.AzureServiceBus`)

- New transport package on Azure.Messaging.ServiceBus 7.21: queue-per-message-type sends,
  topic/subscription fan-out per handler, optional sessions with per-session ordering, native
  `ScheduledEnqueueTime`, managed-identity or connection-string auth, topology provisioning
  through `ServiceBusAdministrationClient`, and handler failures forwarded to the CQRS
  dead-letter store with the original headers before the broker dead-letter call. The shared
  contract suite plus session-ordering, scheduling, and dead-letter tests pass against the
  Testcontainers Service Bus emulator. Targets `net10.0`/`net11.0`; `netstandard2.0` arrives with
  `CQRS-53`.

### AWS SQS/SNS transport (`Jeninnet.CQRS.Transport.Aws`)

- New transport package on AWSSDK.SQS/SNS 4.1: one SQS queue per message type for point-to-point
  sends, one SNS topic per type with a raw-delivery SQS subscriber queue per handler for fan-out,
  optional FIFO message groups from `PartitionKey`, visibility renewal while handlers run, and
  transparent claim-check handoff when a payload exceeds the SQS/SNS cap. Credentials use the
  standard AWS SDK chain; `ServiceUrl` supports LocalStack/VPC endpoints. The shared contract
  suite plus a 300 KB claim-check round trip, a competing-receiver visibility test, and FIFO
  ordering run against LocalStack. Targets `net10.0`/`net11.0`; `netstandard2.0` arrives with
  `CQRS-53`.

---

## [1.0.0] - 2026-11-18

**Status:** Historical pre-parity baseline; never published to NuGet. The first public release is
`1.0.0-preview.1`.

### Validation extracted to a standalone repository

- The validation engine (`Jeninnet.Validation`, `Jeninnet.Validation.Abstraction`,
  `Jeninnet.Validation.Analyzers`) and its samples
  (`Jeninnet.Validation.Analyzers.Sample.Console`, `Jeninnet.Validation.Sample.Console`,
  `Jeninnet.Validation.Sample.Domain`) were removed from this repository. They now live in
  [`Jeninnet.Validation`](https://github.com/TarekNajem04/Jeninnet.Validation), which ships
  25 packages and is published on NuGet as `1.0.0-preview.1`.
- `Jeninnet.CQRS.Validation` now consumes the standalone library through the
  `Jeninnet.Validation.Abstractions` `1.0.0-preview.1` package instead of a project reference.
  The pipeline behavior was adapted to the new engine contracts (`ValueTask` async path,
  `ValidationErrorCollector`-based `ValidationResult`, `ValidationException.Errors`).
- The solution targets `net11.0` with C# 15, matching the standalone validation library.

### Test assertions

- All test projects (`Jeninnet.CQRS.Tests`, `Jeninnet.CQRS.IntegrationTests`,
  `Jeninnet.CQRS.Validation.Tests`) now use
  [`Jeninnet.Assertions`](https://github.com/TarekNajem04/Jeninnet.Assertions) (`1.0.0`)
  with the MSTest adapter (`Jeninnet.Assertions.MSTest`), following the same setup used by
  `Jeninnet.Validation`, `Jeninnet.Localization`, `Jeninnet.FileQuery`, and
  `Jenin.FontAwesome.Blazor`. Every legacy `Assert.*` call was replaced with
  `MSTestAssertions.That(() => x.Should().*)`.

### Repository infrastructure

- Added the shared repository layout used by the Jeninnet platform repositories:
  `.config/` (local tool manifest), `.github/` (community files, issue templates, CI,
  SonarCloud, and NuGet publish workflows), `scripts/` (`common`, `clean`, `coverage`,
  `format`, `sonar`, `badges`, `security`), `global.json` (.NET 11 SDK), and the root
  community files (`CHANGELOG.md`, `CODE_OF_CONDUCT.md`, `CONTRIBUTING.md`, `ROADMAP.md`).
- The solution file (`Jeninnet.CQRS.slnx`) follows the platform folder organization
  (`/.github/`, `/scripts/`, `/Solution Items/`, `/src/`, `/tests/`, `/samples/`).

### Housekeeping

- Removed the `NU1510` warnings by dropping the explicit `Microsoft.Extensions.Logging.Abstractions`
  (from `Jeninnet.CQRS`) and `Microsoft.Extensions.Options` (from `Jeninnet.CQRS.Resilience`)
  package references; both assemblies ship in the .NET shared framework.