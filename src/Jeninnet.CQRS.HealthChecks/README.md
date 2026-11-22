# Jeninnet.CQRS.HealthChecks

`IHealthCheck` reporting CQRS dispatcher availability and circuit-breaker state.

```csharp
services.AddCqrsHealthChecks(); // registers a "cqrs" check
services.AddCqrsSagaHealthChecks(); // registers a "sagas" check when ISagaStore exists
services.AddCqrsRiderHealthChecks(); // registers a "riders" check per named rider
services.AddCqrsRetentionHealthChecks(); // registers a "retention" freshness check
app.MapHealthChecks("/healthz");
```

- `Unhealthy` when no `ICqrsBroker` is registered.
- `Degraded` while any registered circuit breaker is open (names in `Data["openBreakers"]`).
- `sagas` is `Unhealthy` when an active saga has not changed for `SagaHealthOptions.StuckAfter`
  (default 10 minutes); terminal states declared with `Completed()`/`Failure()` are ignored.
- `routing-slips` is `Unhealthy` when a running or compensating slip has not changed for
  `RoutingSlipHealthOptions.StuckAfter` (default 10 minutes); terminal slips are ignored.
- `scheduler` reports due scheduler backlog, degraded/unhealthy at
  `SchedulerHealthOptions.DueDegraded`/`DueUnhealthy` (defaults 100/1000).
- `riders` is `Degraded` before/while riders start or stop, `Unhealthy` when one faulted, and
  `Healthy` when every rider is started; `Data` carries `rider.{name}` for each rider.
- `retention` is `Degraded` before the first successful sweep, after a store failure, or when the
  last success is older than `RetentionHealthOptions.MaxLag` (default two hours).
- `Healthy` otherwise.