# Jeninnet.CQRS.Idempotency.EntityFrameworkCore

Durable `IIdempotencyStore` on Entity Framework Core for the Jeninnet CQRS pipeline.

> **Target framework:** `net11.0` only. Microsoft.EntityFrameworkCore 11 does not ship a
> `netstandard2.0` asset; the dependency, consequences, and revisit trigger are recorded in
> [ADR-0004](https://github.com/TarekNajem04/Jeninnet.CQRS/blob/main/docs/adr/ADR-0004-efcore-netstandard2.md).
> The in-memory stores remain portable.
One row per idempotency key (key + response payload + expiry); concurrent duplicate
stores converge on one winner without surfacing an exception.

The same `DbContext` also hosts the durable deferred-delivery log
(`DeferredCommands`): scheduled commands survive restarts and are replayed at
startup. Register it with `AddCqrsEfDeferredDelivery` (which also wires the
in-process pump):

```csharp
builder.Services.AddCqrsEfDeferredDelivery(
    db => db.UseSqlite("Data Source=idempotency.db"));
```

Without arguments it only replaces the in-memory durability log; register the
`IDbContextFactory<IdempotencyDbContext>` yourself first. Command types are
resolved by assembly-qualified name on replay. A row whose command type no
longer resolves is surfaced as `UnresolvedCommandEnvelope` and dead-lettered by
the pump, not silently dropped: an accepted message stays inspectable. Corrupt
payloads are completed (dropped) because they can never execute. `TimeProvider.System`
is used unless registered; tests can substitute a fake clock.

Rehydration is **registry first**. When the application assembly uses the CQRS
source generator, `AddCqrs()` registers a generated `IDeferredEnvelopeFactory`
covering every `ICommand`/`ICommand<T>` declared in that compilation. The store
asks it to rebuild the payload and only then falls back to the reflection path in
`DeferredEnvelopeSerializer`, which is annotated with
`[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`. Commands declared in a
referenced contracts assembly are not in the registry; declare them in the
application assembly (or implement `IDeferredEnvelopeFactory` there) when the
deployment is trimmed or NativeAOT. The EF change tracker itself remains outside
the NativeAOT story (use compiled models); the generated registry is what makes
the *rehydration* step reflection-free.

Failed deferred commands land in a dead-letter store instead of disappearing
(`DeadLetters`). The EF Core store is registered automatically by
`AddCqrsEfDeferredDelivery`; register it alone with `AddCqrsEfDeadLetters`.
Each row keeps the original payload, an attempt count, and the last error.
Inspect and replay through the dispatcher:

```csharp
var failed = await deadLetters.ListAsync(ct);
var replayed = await dispatcher.ReplayAsync(failed[0].Key, ct);
```

A successful replayed execution removes the entry; a failure increments
`Attempts` and updates the last error. Retention is bounded by
`DeadLetterOptions.Retention` (default 30 days) and `MaxEntries` (default
1000), enforced on write and by the hosted pruning service.

Notifications can be staged in a durable outbox instead of published inside a
transaction. Register it with `AddCqrsEfNotificationOutbox` when you also use
`AddCqrsTransactionBehavior`; a rollback discards the queued notification and
a commit stages then publishes it. Delivery is at-least-once:

```csharp
builder.Services.AddCqrsEfNotificationOutbox(
    db => db.UseSqlite("Data Source=idempotency.db"));
```

To deliver the outbox through a broker instead of the in-process relay, register
the transport first and add the leased relay worker:

```csharp
builder.Services.AddCqrsRabbitMq(...);
builder.Services.AddCqrsOutboxRelay<RabbitMqTransport>(options => {
    options.BatchSize = 32;
    options.MaxAttempts = 10;
});
```

The worker leases rows with `LeaseOwner`/`LeaseExpiresAtUtc`, publishes through
`IMessageTransport`, and completes only after the broker accepts the message.
Failures persist `Attempts`, `NextAttemptUtc`, and the last error; at
`MaxAttempts` the row moves to `IDeadLetterStore`. Rows are delivered in staged
order: a leased or backing-off head blocks later rows. Two workers on one
database cannot own the same row (conditional updates guarded by `Version`).

## Retention and archives

Retention is opt-in and dry-run by default. `AddCqrsEfRetention` registers the inbox,
dead-letter, job, and outbox archive adapters plus the core hosted sweeper:

```csharp
builder.Services.AddCqrsEfRetention(configure: options => {
    options.Interval = TimeSpan.FromHours(1);
    options.BatchSize = 100;
    options.DryRun = false;
    options.AddPolicy(RetentionStoreNames.Inbox, new RetentionPolicy { Retention = TimeSpan.FromDays(7) });
    options.AddPolicy(RetentionStoreNames.DeadLetters, new RetentionPolicy {
        Retention = TimeSpan.FromDays(30),
        GracePeriod = TimeSpan.FromDays(1),
    });
});
```

`InboxArchive`, `DeadLetterArchive`, `JobArchive`, and `OutboxArchive` keep the source schema plus
`ArchivedAtUtc`; `EnsureCreated` creates them. Completed outbox rows keep a `CompletedAtUtc` until
they are archived, but only when this extension is registered. Existing databases need a
migration/`ALTER TABLE` for `OutboxNotifications.CompletedAtUtc` and the archive tables. A dry run
returns the same candidate/archived counts without writing. Stores without a policy are never
touched.

Payloads above a size threshold can be kept outside the message rows. Register
the claim-check store once; every EF store then writes a reference instead of
the payload:

```csharp
builder.Services.AddCqrsEfClaimCheck(
    db => db.UseSqlite("Data Source=idempotency.db"),
    claim => claim.ThresholdBytes = 64 * 1024);
```

Serialization is a seam (`IMessageSerializer`); the default is UTF-8 JSON with
web defaults. Register a custom serializer before or after the EF extensions to
replace it. Claim-checked payloads record the serializer content type and
refuse to deserialize under a different one.

## Registration (idempotency)

```csharp
builder.Services.AddCqrsEfIdempotencyStore(
    db => db.UseSqlite("Data Source=idempotency.db"),
    store => store.TimeToLive = TimeSpan.FromDays(7));
```

Without arguments it only replaces `IIdempotencyStore` and wires
`IdempotencyBehavior` — register the `IDbContextFactory<IdempotencyDbContext>`
yourself first. `TimeProvider.System` is used unless registered; tests can
substitute a fake clock.

## Execution lease (CQRS-40)

The durable store also implements the atomic execution lease used by
`IdempotencyBehavior`: `TryAcquireAsync` inserts a lease row or reclaims an expired one,
`RenewAsync` extends it, and `CompleteAsync` stores the response only while the caller still
owns the row. This is what prevents two concurrent dispatches with the same key from both
running the handler. The atomicity requirement is a unique primary key plus the
`Version` optimistic-concurrency token; concurrent losers retry and observe `Busy`.

Lease policy lives in `IdempotencyBehaviorOptions`:

- `WaitTimeout` (default 30 s) bounds how long a duplicate waits for the stored response.
- `LeaseTimeToLive` (default 30 s) bounds how long a crashed owner blocks the key.
- `HeartbeatInterval` is off by default; when set it must be shorter than the TTL and renews the
  lease in the background while the handler runs.
- `FailFastWhenBusy` throws `ConcurrentExecutionException` immediately instead of waiting.

Exactly-once holds while the owner completes inside the lease and the response write succeeds.
A crash after a side effect but before completion is at-least-once: the lease expires and the
next caller executes again. Configure the TTL above normal handler duration (or enable the
heartbeat) and treat that boundary explicitly.

```csharp
builder.Services.AddCqrsEfIdempotencyStore(
    db => db.UseSqlite("Data Source=idempotency.db"),
    configureBehavior: behavior => {
        behavior.WaitTimeout = TimeSpan.FromSeconds(10);
        behavior.HeartbeatInterval = TimeSpan.FromSeconds(10);
    });
```

## Consumer inbox

`AddCqrsEfInbox` registers the consumer-inbox middleware and replaces the in-memory store with
`EfInboxStore`, so a broker redelivery after a restart still sees the processed message id:

```csharp
builder.Services.AddCqrsEfInbox(
    db => db.UseSqlite("Data Source=idempotency.db"),
    inbox => {
        inbox.Retention = TimeSpan.FromDays(14);
        inbox.PendingTimeout = TimeSpan.FromMinutes(2);
    });
```

The first delivery of a message id runs the handler; a duplicate is acknowledged without invoking
it. A handler failure abandons the pending row so the broker can retry immediately. If the process
crashes after the handler succeeds but before the row completes, the row becomes reclaimable after
`PendingTimeout` and the redelivery runs again (at-least-once). Set `AutoComplete = false` and call
`IInboxStore.CompleteAsync` inside the handler's transaction to close that window. The background
pruner removes processed rows past `Retention` and abandoned pending rows past `PendingTimeout`;
the duplicate counter is `cqrs.inbox.duplicates`.

## Background jobs

`AddCqrsEfJobs` replaces the in-memory job store with `EfJobStore`, so submitted jobs, progress,
leases, and terminal status survive a restart:

```csharp
builder.Services.AddCqrsEfJobs(
    db => db.UseSqlite("Data Source=idempotency.db"),
    jobs => jobs.MaxAttempts = 5);
```

Register handlers with `AddJobHandler<TJob, THandler>()`. The worker leases a row to one owner,
renews the lease while the handler runs, retries failures with exponential backoff, and writes
exhausted jobs to the dead-letter store. Terminal rows are pruned after `JobOptions.Retention`.
Pair it with `AddCqrsEfDeadLetters` when terminal failures must survive a restart.

## Multi-tenancy

Register `AddCqrsMultiTenancy<TResolver>()` before the EF stores. `IdempotencyDbContext` reads
the ambient tenant once per short-lived context and applies a global query filter to every
message table, so a scoped `List`/`Get` cannot see another tenant's rows. Inserts stamp the
ambient tenant; background workers open a tenant scope from the row they are processing, so
completion, failure, schedule re-arming, and outbox lease updates target the right partition.

`Records`, `DeferredCommands`, `ScheduledMessages`, and `Inbox` use composite primary keys
because their keys are caller- or broker-supplied. Other tables keep generated keys and use the
tenant column only for filtering. No ambient tenant means the global/empty partition; that is the
backward-compatible single-tenant mode and the mode used by unscoped maintenance scans.

## Schema

`EnsureCreatedAsync` is enough for SQLite/single-node deployments:

```csharp
await using var db = await factory.CreateDbContextAsync();
await db.Database.EnsureCreatedAsync();
```

Managed-migration equivalent (SQL Server shown; adjust types per provider):

```sql
CREATE TABLE Records (
    TenantId NVARCHAR(128) NOT NULL,
    [Key] NVARCHAR(256) NOT NULL,
    ResponseType NVARCHAR(1024) NOT NULL,
    Payload NVARCHAR(MAX) NOT NULL,
    ClaimCheckKey NVARCHAR(256) NULL,
    StoredAtUtc DATETIMEOFFSET NOT NULL,
    ExpiresAtUtc DATETIMEOFFSET NULL,
    LeaseOwner NVARCHAR(256) NULL,
    LeaseExpiresAtUtc DATETIMEOFFSET NULL,
    Version BIGINT NOT NULL,
    CONSTRAINT PK_Records PRIMARY KEY (TenantId, [Key])
);
CREATE INDEX IX_Records_ExpiresAtUtc ON Records (ExpiresAtUtc);
CREATE INDEX IX_Records_LeaseExpiresAtUtc ON Records (LeaseExpiresAtUtc);
```

Deferred commands live in a second table in the same database (one migration
story for the whole package):

```sql
CREATE TABLE DeferredCommands (
    TenantId NVARCHAR(128) NOT NULL,
    [Key] NVARCHAR(256) NOT NULL,
    CommandType NVARCHAR(1024) NOT NULL,
    Payload NVARCHAR(MAX) NOT NULL,
    ClaimCheckKey NVARCHAR(256) NULL,
    MessageVersion INT NULL,
    RecurringKey NVARCHAR(256) NULL,
    CronExpression NVARCHAR(128) NULL,
    TimeZoneId NVARCHAR(128) NULL,
    RecurringOverlap INT NULL,
    DueUtc DATETIMEOFFSET NOT NULL,
    StoredAtUtc DATETIMEOFFSET NOT NULL,
    ExpiresAtUtc DATETIMEOFFSET NULL,
    Version BIGINT NOT NULL,
    CONSTRAINT PK_DeferredCommands PRIMARY KEY (TenantId, [Key])
);
CREATE INDEX IX_DeferredCommands_DueUtc ON DeferredCommands (DueUtc);
CREATE INDEX IX_DeferredCommands_ExpiresAtUtc ON DeferredCommands (ExpiresAtUtc);
```

Dead letters live in a third table in the same database:

```sql
CREATE TABLE DeadLetters (
    TenantId NVARCHAR(128) NOT NULL,
    [Key] NVARCHAR(256) NOT NULL PRIMARY KEY,
    CommandType NVARCHAR(1024) NOT NULL,
    Payload NVARCHAR(MAX) NOT NULL,
    ClaimCheckKey NVARCHAR(256) NULL,
    DueUtc DATETIMEOFFSET NOT NULL,
    ExpiresAtUtc DATETIMEOFFSET NULL,
    Attempts INT NOT NULL,
    LastErrorType NVARCHAR(1024) NOT NULL,
    LastErrorMessage NVARCHAR(2048) NOT NULL,
    FirstFailedAtUtc DATETIMEOFFSET NOT NULL,
    LastFailedAtUtc DATETIMEOFFSET NOT NULL
);
CREATE INDEX IX_DeadLetters_LastFailedAtUtc ON DeadLetters (LastFailedAtUtc);
```

Outbox notifications live in a fourth table:

```sql
CREATE TABLE OutboxNotifications (
    TenantId NVARCHAR(128) NOT NULL,
    [Key] NVARCHAR(256) NOT NULL PRIMARY KEY,
    NotificationType NVARCHAR(1024) NOT NULL,
    Payload NVARCHAR(MAX) NOT NULL,
    ClaimCheckKey NVARCHAR(256) NULL,
    StagedAtUtc DATETIMEOFFSET NOT NULL,
    LeaseOwner NVARCHAR(256) NULL,
    LeaseExpiresAtUtc DATETIMEOFFSET NULL,
    Attempts INT NOT NULL,
    NextAttemptUtc DATETIMEOFFSET NULL,
    LastErrorType NVARCHAR(1024) NULL,
    LastErrorMessage NVARCHAR(2048) NULL,
    Version BIGINT NOT NULL
);
CREATE INDEX IX_OutboxNotifications_StagedAtUtc ON OutboxNotifications (StagedAtUtc);
CREATE INDEX IX_OutboxNotifications_LeaseExpiresAtUtc ON OutboxNotifications (LeaseExpiresAtUtc);
CREATE INDEX IX_OutboxNotifications_NextAttemptUtc ON OutboxNotifications (NextAttemptUtc);
```

Claim-checked payloads live in a fifth table:

```sql
CREATE TABLE ClaimChecks (
    [Key] NVARCHAR(256) NOT NULL PRIMARY KEY,
    ContentType NVARCHAR(256) NOT NULL,
    Payload VARBINARY(MAX) NOT NULL,
    CreatedAtUtc DATETIMEOFFSET NOT NULL
);
```

Pending request/response rows live in a sixth table (enable it with
`AddCqrsEfRequestResponse`):

```sql
CREATE TABLE RequestInbox (
    TenantId NVARCHAR(128) NOT NULL,
    RequestId NVARCHAR(256) NOT NULL PRIMARY KEY,
    RequestType NVARCHAR(1024) NOT NULL,
    ResponseType NVARCHAR(1024) NOT NULL,
    Mode INT NOT NULL,
    CreatedAtUtc DATETIMEOFFSET NOT NULL,
    ExpiresAtUtc DATETIMEOFFSET NOT NULL
);
CREATE INDEX IX_RequestInbox_ExpiresAtUtc ON RequestInbox (ExpiresAtUtc);
```

Persistent schedules live in a seventh table (enable it with `AddCqrsEfScheduler`):

```sql
CREATE TABLE ScheduledMessages (
    TenantId NVARCHAR(128) NOT NULL,
    [Key] NVARCHAR(256) NOT NULL,
    MessageType NVARCHAR(1024) NOT NULL,
    ContentType NVARCHAR(256) NOT NULL,
    Payload VARBINARY(MAX) NOT NULL,
    Kind INT NOT NULL,
    NextRunUtc DATETIMEOFFSET NOT NULL,
    NextRunTicks BIGINT NOT NULL,
    CreatedAtUtc DATETIMEOFFSET NOT NULL,
    UpdatedAtUtc DATETIMEOFFSET NOT NULL,
    CronExpression NVARCHAR(128) NULL,
    TimeZoneId NVARCHAR(128) NULL,
    Attempts INT NOT NULL,
    Version BIGINT NOT NULL,
    LeaseOwner NVARCHAR(256) NULL,
    LeaseExpiresAtUtc DATETIMEOFFSET NULL,
    LeaseExpiresTicks BIGINT NULL,
    LastErrorType NVARCHAR(1024) NULL,
    LastErrorMessage NVARCHAR(2048) NULL,
    CONSTRAINT PK_ScheduledMessages PRIMARY KEY (TenantId, [Key])
);
CREATE INDEX IX_ScheduledMessages_NextRunUtc ON ScheduledMessages (NextRunUtc);
CREATE INDEX IX_ScheduledMessages_NextRunTicks ON ScheduledMessages (NextRunTicks);
CREATE INDEX IX_ScheduledMessages_LeaseExpiresTicks ON ScheduledMessages (LeaseExpiresTicks);
```

Consumer-inbox rows live in an eighth table (enable it with `AddCqrsEfInbox`):

```sql
CREATE TABLE Inbox (
    TenantId NVARCHAR(128) NOT NULL,
    MessageId NVARCHAR(256) NOT NULL,
    ReceivedAtUtc DATETIMEOFFSET NOT NULL,
    ProcessedAtUtc DATETIMEOFFSET NULL,
    ResultHash NVARCHAR(128) NULL,
    ExpiresAtUtc DATETIMEOFFSET NULL,
    Version BIGINT NOT NULL,
    CONSTRAINT PK_Inbox PRIMARY KEY (TenantId, MessageId)
);
CREATE INDEX IX_Inbox_ProcessedAtUtc ON Inbox (ProcessedAtUtc);
CREATE INDEX IX_Inbox_ExpiresAtUtc ON Inbox (ExpiresAtUtc);
```

Background jobs live in a ninth table (enable it with `AddCqrsEfJobs`):

```sql
CREATE TABLE Jobs (
    JobId NVARCHAR(64) NOT NULL PRIMARY KEY,
    JobType NVARCHAR(1024) NOT NULL,
    Payload VARBINARY(MAX) NOT NULL,
    TenantId NVARCHAR(128) NOT NULL,
    Status INT NOT NULL,
    Progress INT NOT NULL,
    Attempts INT NOT NULL,
    MaxAttempts INT NOT NULL,
    CreatedAtUtc DATETIMEOFFSET NOT NULL,
    UpdatedAtUtc DATETIMEOFFSET NOT NULL,
    NextAttemptUtc DATETIMEOFFSET NOT NULL,
    StartedAtUtc DATETIMEOFFSET NULL,
    CompletedAtUtc DATETIMEOFFSET NULL,
    LeaseOwner NVARCHAR(256) NULL,
    LeaseExpiresAtUtc DATETIMEOFFSET NULL,
    CancellationRequested BIT NOT NULL,
    LastErrorType NVARCHAR(1024) NULL,
    LastErrorMessage NVARCHAR(2048) NULL,
    Version BIGINT NOT NULL
);
CREATE INDEX IX_Jobs_Status ON Jobs (Status);
CREATE INDEX IX_Jobs_NextAttemptUtc ON Jobs (NextAttemptUtc);
CREATE INDEX IX_Jobs_LeaseExpiresAtUtc ON Jobs (LeaseExpiresAtUtc);
CREATE INDEX IX_Jobs_CompletedAtUtc ON Jobs (CompletedAtUtc);
```

### Migrating an existing database

`ClaimCheckKey` is nullable, so old rows keep reading: a row with a key loads
the payload from `ClaimChecks`, anything else deserializes the inline
`Payload` (UTF-8 JSON, or Base64 behind the `~b64~` marker for binary
serializers). Add the column and the table:

```sql
ALTER TABLE Records ADD ClaimCheckKey NVARCHAR(256) NULL;
ALTER TABLE DeferredCommands ADD ClaimCheckKey NVARCHAR(256) NULL;
ALTER TABLE DeadLetters ADD ClaimCheckKey NVARCHAR(256) NULL;
ALTER TABLE OutboxNotifications ADD ClaimCheckKey NVARCHAR(256) NULL;
-- plus CREATE TABLE ClaimChecks above
-- plus CREATE TABLE RequestInbox above when request/response is enabled
-- plus CREATE TABLE ScheduledMessages above when the scheduler is enabled
-- plus CREATE TABLE Inbox above when the consumer inbox is enabled
-- plus CREATE TABLE Jobs above when background jobs are enabled
```

Tenant isolation adds one column to every message row. Existing rows belong to the global
(empty-tenant) partition, so a non-tenant host keeps reading them; a tenant-scoped host sees
only its own rows:

```sql
ALTER TABLE Records ADD TenantId NVARCHAR(128) NOT NULL DEFAULT '';
ALTER TABLE DeferredCommands ADD TenantId NVARCHAR(128) NOT NULL DEFAULT '';
ALTER TABLE DeadLetters ADD TenantId NVARCHAR(128) NOT NULL DEFAULT '';
ALTER TABLE OutboxNotifications ADD TenantId NVARCHAR(128) NOT NULL DEFAULT '';
ALTER TABLE RequestInbox ADD TenantId NVARCHAR(128) NOT NULL DEFAULT '';
ALTER TABLE ScheduledMessages ADD TenantId NVARCHAR(128) NOT NULL DEFAULT '';
```

`Records`, `DeferredCommands`, and `ScheduledMessages` use caller-supplied keys, so their primary
key becomes `(TenantId, Key)`. Drop the existing primary key and recreate it with the tenant
first; the SQL Server constraint name is usually `PK_<Table>`. This permits two tenants to own
the same caller key without a collision. Generated keys (GUIDs, timestamp keys) keep their
single-column primary key and rely on the query filter plus explicit tenant predicates on bulk
updates and deletes.

Transport relay leases add their columns and indexes (old rows read as free,
attempt zero, version zero):

```sql
ALTER TABLE OutboxNotifications ADD LeaseOwner NVARCHAR(256) NULL;
ALTER TABLE OutboxNotifications ADD LeaseExpiresAtUtc DATETIMEOFFSET NULL;
ALTER TABLE OutboxNotifications ADD Attempts INT NOT NULL DEFAULT 0;
ALTER TABLE OutboxNotifications ADD NextAttemptUtc DATETIMEOFFSET NULL;
ALTER TABLE OutboxNotifications ADD LastErrorType NVARCHAR(1024) NULL;
ALTER TABLE OutboxNotifications ADD LastErrorMessage NVARCHAR(2048) NULL;
ALTER TABLE OutboxNotifications ADD Version BIGINT NOT NULL DEFAULT 0;
CREATE INDEX IX_OutboxNotifications_LeaseExpiresAtUtc ON OutboxNotifications (LeaseExpiresAtUtc);
CREATE INDEX IX_OutboxNotifications_NextAttemptUtc ON OutboxNotifications (NextAttemptUtc);
```

Execution leases add two nullable columns (old rows read as free) and their index:

```sql
ALTER TABLE Records ADD LeaseOwner NVARCHAR(256) NULL;
ALTER TABLE Records ADD LeaseExpiresAtUtc DATETIMEOFFSET NULL;
CREATE INDEX IX_Records_LeaseExpiresAtUtc ON Records (LeaseExpiresAtUtc);
```

Deferred rows also carry `MessageVersion` (nullable; old rows mean version 1). Register
`IMessageUpgrader` implementations to evolve payloads; the EF deferred store applies the chain
before rebuilding the envelope, writes the current version for new rows, and surfaces a row
newer than the reader as an unknown-version envelope so the pump dead-letters it instead of
dropping it:

```csharp
builder.Services.AddTransient<IMessageUpgrader, OrderV1ToV2Upgrader>();
```

```sql
ALTER TABLE DeferredCommands ADD MessageVersion INT NULL;
ALTER TABLE DeferredCommands ADD RecurringKey NVARCHAR(256) NULL;
ALTER TABLE DeferredCommands ADD CronExpression NVARCHAR(128) NULL;
ALTER TABLE DeferredCommands ADD TimeZoneId NVARCHAR(128) NULL;
ALTER TABLE DeferredCommands ADD RecurringOverlap INT NULL;
```

Removing a row (completion, expiry prune, dead-letter removal, overwrite)
deletes its claim as well, so the table does not accumulate orphans.

## SQLite concurrency

SQLite has a single writer. For concurrent dispatches enable WAL plus a busy
timeout once per database (or set them on every connection):

```sql
PRAGMA journal_mode=WAL;
PRAGMA busy_timeout=5000;
```

The store additionally retries lost optimistic-concurrency races (bounded by
`EfIdempotencyStoreOptions.MaxAttempts`, default 10).

## Expiry cleanup

Entries with `ExpiresAtUtc <= now` read as a miss and are pruned on read.
Reclaim space periodically:

```csharp
var removed = await store.PruneExpiredAsync(ct);
```

## Notes

- A response stored under one `TResponse` reads as a miss under another type
  (same semantics as the in-memory default). Reusing a key with a different response
  type lets the next acquire overwrite it; keys must identify one command shape.
- Expired leases are reclaimed lazily on the next `TryAcquireAsync`; orphan lease rows
  do not block the key after their `LeaseExpiresAtUtc`.
- NativeAOT: EF Core change tracking is reflection-based; use compiled models
  (`dotnet ef dbcontext optimize`) for trimming-safe deployment. Status is
  recorded per-behavior by CQRS-10, not re-proven here.