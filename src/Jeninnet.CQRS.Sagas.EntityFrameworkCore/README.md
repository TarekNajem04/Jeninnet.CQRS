# Jeninnet.CQRS.Sagas.EntityFrameworkCore

Durable `ISagaStore` for `Jeninnet.CQRS.Sagas` on Entity Framework Core. One row stores a saga id,
the state type, the serialized state, and a version token; concurrent writers are resolved by the
version token and surface `SagaConcurrencyException`.

```csharp
builder.Services.AddCqrs();
builder.Services.AddCqrsEfSagas<OrderSaga>(
    db => db.UseSqlite("Data Source=sagas.db"));
```

`AddCqrsEfSagas<T>()` calls `AddCqrsSagas<T>()` and replaces the in-memory store with
`EfSagaStore` (last registration wins). Without `configureDb`, register
`IDbContextFactory<SagaDbContext>` yourself first. The package targets `net11.0` because EF Core 11
ships no `net10.0` leg.

## Schema

Use `EnsureCreatedAsync` for SQLite/single-node deployments, or apply the managed-migration
equivalent:

```sql
CREATE TABLE Sagas (
    StateType NVARCHAR(1024) NOT NULL,
    SagaId NVARCHAR(256) NOT NULL,
    State VARBINARY(MAX) NOT NULL,
    CurrentState NVARCHAR(128) NOT NULL,
    Progress NVARCHAR(MAX) NOT NULL,
    Version BIGINT NOT NULL,
    CreatedAtUtc DATETIMEOFFSET NOT NULL,
    UpdatedAtUtc DATETIMEOFFSET NOT NULL,
    CONSTRAINT PK_Sagas PRIMARY KEY (StateType, SagaId)
);
CREATE INDEX IX_Sagas_UpdatedAtUtc ON Sagas (UpdatedAtUtc);
```

The composite key lets two machines reuse the same correlation id without sharing state. `State`
is produced by the configured `IMessageSerializer`; the default is UTF-8 JSON. `CurrentState` is
copied out for health queries and `Progress` holds the JSON completed/compensated step ids.
`Version` is the optimistic-concurrency token: a save writes `Version + 1` only when the expected
version still matches.

Existing databases need the two progress columns:

```sql
ALTER TABLE Sagas ADD CurrentState NVARCHAR(128) NOT NULL DEFAULT '';
ALTER TABLE Sagas ADD Progress NVARCHAR(MAX) NOT NULL DEFAULT '{}';
```

## Retention

`AddCqrsEfSagaRetention` registers the saga archive adapter and the core hosted sweeper. Only
sagas in a machine-declared terminal state and older than the policy cutoff are archived:

```csharp
builder.Services.AddCqrsEfSagaRetention(configure: options =>
    options.AddPolicy(RetentionStoreNames.Sagas, new RetentionPolicy {
        Retention = TimeSpan.FromDays(30),
    }));
```

The `SagaArchive` table keeps the source columns plus `ArchivedAtUtc`; `EnsureCreated` creates it.
Dry-run is the core default. A saga state type without a registered machine is never archived
because the store cannot prove it is terminal.

## Notes

- SQLite has a single writer; enable WAL and a busy timeout for concurrent hosts, as in the
  idempotency package.
- EF Core change tracking is reflection-based. The saga machine and dispatcher themselves are
  reflection-free and AOT-friendly; the EF store follows the package's existing AOT boundary.
- Follow-up commands are dispatched after the state save, so a crash after the save but before the
  command is at-least-once. Make follow-up handlers idempotent. Timeouts and compensation use the
  same durable state/progress rows.