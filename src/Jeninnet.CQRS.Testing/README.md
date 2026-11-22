# Jeninnet.CQRS.Testing

Test kit for the Jeninnet CQRS pipeline: a recording broker plus specification-style
assertions on top of `Jeninnet.Assertions`.

```csharp
var broker = new TestCqrsBroker(provider.GetRequiredService<ICqrsBroker>());

await broker.DispatchAsync(new CreateOrder("O-1"), TestContext.CancellationToken);

broker.ShouldHaveDispatched<CreateOrder>(c => c.Id == "O-1");
broker.ShouldNotHavePublished<OrderShipped>();
```

Register `SpyBehavior<TRequest, TResponse>` with a shared `BehaviorCallLog` to prove pipeline
traversal, then assert `log.ShouldHaveRunBehavior<SpyBehavior<CreateOrder, Unit>>()`.
Failure messages list what was actually dispatched — no `Assert` archaeology.

## Saga test harness

`SagaTestHarness<TState, TEvent>` builds the real dispatcher around an in-memory store, records
every follow-up command and notification, and returns the typed state after each event. No host,
container, or handler registration is required:

```csharp
using var harness = new SagaTestHarness<OrderSagaState, OrderPlaced>(new OrderSaga());

var created = await harness.When(new OrderPlaced("O-1"));
created.ShouldHaveTransitionedTo("AwaitingPayment");
created.ShouldHavePublished<ReserveStock>(m => m.OrderId == "O-1");

var completed = await harness.WhenAsync(new PaymentTaken("O-1", "P-1"), typeof(PaymentTaken));
completed.ShouldHaveTransitionedTo("Completed");
completed.ShouldHavePublished<ShipOrder>();
```

Pass an `EfSagaStore` as the second constructor argument to exercise durable persistence.
`ISagaRepository<TState>` wraps any `ISagaStore`; the in-memory and EF stores pass the same
repository contract suite, including stale-version rejection.