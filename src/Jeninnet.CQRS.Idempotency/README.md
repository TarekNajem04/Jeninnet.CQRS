# Jeninnet.CQRS.Idempotency

Idempotent command handling for the Jeninnet CQRS pipeline. A command implements
`IIdempotentCommand<TResponse>` and supplies an `IdempotencyKey`; the behavior returns the stored
response for a repeated key instead of running the handler again.

```csharp
public sealed record PlaceOrder(string Key) : IIdempotentCommand<OrderId> {
    public string IdempotencyKey => Key;
}

builder.Services.AddCqrsIdempotencyBehavior(options => {
    options.WaitTimeout = TimeSpan.FromSeconds(30);
    options.LeaseTimeToLive = TimeSpan.FromSeconds(30);
});
```

## Execution lease (CQRS-40)

`IIdempotencyStore` exposes an explicit lease state machine:

- `TryAcquireAsync<TResponse>(key, owner, ttl)` → `Acquired`, `Busy(owner)`, or `Completed(response)`
- `RenewAsync(key, owner, ttl)` → extends an unexpired lease owned by `owner`
- `CompleteAsync(key, owner, response)` → stores the result only while `owner` still holds the lease
- `ReleaseAsync(key, owner)` → abandons a lease after a handler failure

`IdempotencyBehavior` leases the key before invoking the handler. Concurrent callers either wait
for the stored response (`WaitTimeout`, default 30 s, polling every 50 ms) or fail fast with
`ConcurrentExecutionException` when `FailFastWhenBusy` is set. Only one caller runs the handler;
everyone else returns that response.

### TTL and heartbeat policy

- `LeaseTimeToLive` (default 30 s) bounds how long a crashed owner can block a key. The next
  caller reclaims an expired lease atomically.
- `HeartbeatInterval` is **off by default**. When set (recommended: one third of the TTL), the
  behavior renews the lease in the background while the handler runs; a failed renewal stops the
  heartbeat and leaves the lease to expire.
- Without a heartbeat, keep `LeaseTimeToLive` above the handler's worst-case duration.
- Exactly-once holds while the owner completes inside the lease and the response write succeeds.
  A crash after a side effect but before completion is at-least-once: the lease expires and the
  next caller executes again. Document that boundary instead of pretending distributed exactly-once
  without a transactional resource.

### Atomicity requirement

`TryAcquireAsync` must be atomic per key. The in-memory store locks a per-key entry; the EF Core
store relies on a unique primary key plus an optimistic-concurrency token, which makes acquisition
atomic across processes sharing the database. A custom store must provide equivalent guarantees.

The in-memory store completes responses for the life of the process. Use the EF Core store when
idempotency must survive restarts.
