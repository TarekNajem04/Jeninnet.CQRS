# Jeninnet.CQRS.Sample.EndToEnd

End-to-end CQRS demo: validation, retry, caching, and idempotency composed in
one pipeline with `IOptions` configuration. Every flow self-verifies with
`Jeninnet.Assertions` — a non-zero exit code means a flow regressed.

## Run

```powershell
dotnet run --project samples/Jeninnet.CQRS.Sample.EndToEnd -f net11.0
```

Expected output (log lines omitted):

```text
=== Jeninnet CQRS end to end ===
[1/4] Validation failed as expected: Validation failed. 1 error(s) found.
[2/4] Retry succeeded after 3 attempts: pong:hello
[3/4] Cache hit: handler ran 1 time(s).
[4/4] Idempotent replay: handler ran 1 time(s).
=== All four flows verified ===
```

## What each step proves

1. **Validation failure** — an empty customer name fails `CreateOrderValidator`;
   the `ValidationBehavior` throws before the handler runs (`Executions == 0`).
2. **Retry success** — `FlakyPingHandler` throws twice, then succeeds.
   `RetryBehaviorOptions` binds from `appsettings.json` through `IOptions`
   (`RetryCount: 5`, 10 ms base delay, breaker off), so attempts land on 3.
3. **Cache hit** — `GetCatalogQuery` implements `ICacheable<string>`; two identical
   dispatches run the handler once.
4. **Idempotent replay** — `PlaceOrderCommand` carries `IdempotencyKey`;
   the second dispatch with key `order-42` returns the stored response without
   re-running the handler.

Registration order matters: validation → resilience → caching → idempotency, so
failures short-circuit before any state-changing behavior runs.