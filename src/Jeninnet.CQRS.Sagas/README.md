# Jeninnet.CQRS.Sagas

Small, explicit saga state machines for `Jeninnet.CQRS`. A saga is a persisted data object plus a
transition table; there is no code generation, reflection, or designer.

```csharp
builder.Services.AddCqrs();                  // dispatchers + command handlers
builder.Services.AddCqrsDeferredDelivery();  // required for OnTimeout
builder.Services.AddCqrsSagas<OrderSaga>();

public sealed class OrderSaga : SagaStateMachine<OrderSagaState, OrderEvent> {
    protected override void Configure() {
        Initially("AwaitingPayment");

        Correlate<OrderPlaced>(OrderEvent.Placed, message => message.OrderId)
            .Initially()
            .Then((state, message) => state.OrderId = message.OrderId)
            .TransitionTo("AwaitingPayment")
            .Compensate("reserve", state => new ReleaseStock(state.OrderId))
            .OnTimeout(TimeSpan.FromMinutes(5), state => new OrderTimedOut(state.OrderId))
            .Publish((state, _) => new ReserveStock(state.OrderId));

        Correlate<PaymentTaken>(OrderEvent.PaymentTaken, message => message.OrderId)
            .InState("AwaitingPayment")
            .TransitionTo("Completed")
            .Completed()
            .Publish((state, _) => new ShipOrder(state.OrderId));

        Correlate<PaymentFailed>(OrderEvent.PaymentFailed, message => message.OrderId)
            .InState("AwaitingPayment")
            .Failure()
            .TransitionTo("Faulted");
    }
}
```

`ISagaState` supplies the mutable `CurrentState` transition-table position. Events are ordinary
notifications; `DefaultNotificationPublisher` calls the saga observer after normal notification
handlers. Commands are published with `Publish`; use `PublishNotification` for events, `Schedule`
for a deferred command, and `OnTimeout` for a timeout event.

## Semantics

- **Correlation:** the first event with an unknown id creates a new saga in the configured initial
  state. A non-initial event for an unknown id is ignored; it never creates a row.
- **Transition table:** an event applies only in the declared state. An out-of-order event is
  ignored without writing or publishing anything.
- **Persistence:** the default `InMemorySagaStore` serializes state through `IMessageSerializer`
  on every save and deserializes on every load, so a failed transition cannot mutate the stored
  copy. Add `Jeninnet.CQRS.Sagas.EntityFrameworkCore` for restart-safe rows.
- **Concurrency:** saves carry the version read before the transition. A conflict reloads and
  re-evaluates, bounded by `SagaOptions.MaxConcurrencyRetries` (default 10), then surfaces
  `SagaConcurrencyException`.
- **Follow-ups:** commands are dispatched only after the state is saved. A follow-up failure
  surfaces to the publisher, but the state is already durable; treat delivery as at-least-once
  and make follow-up handlers idempotent.
- **Configuration:** `Configure` runs lazily on first use or host startup, so a machine may use
  constructor-injected fields. Duplicate ownership of one event message type fails fast.
- **Repository seam:** `ISagaRepository<TState>` / `SagaRepository<TState>` wraps any
  `ISagaStore`; the in-memory and EF stores satisfy the same contract, including optimistic
  concurrency. `SagaTestHarness<TState, TEvent>` from `Jeninnet.CQRS.Testing` runs a machine
  without a host and exposes transition/published assertions.

## Timeouts

`OnTimeout`/`Schedule` persists a `SagaTimeoutCommand` through `IDeferredDispatcher`; the
`SagaTimeoutCommandHandler` rebuilds the event after a restart and feeds it back into the same
transition table. The timeout command requires `AddCqrsDeferredDelivery()` (or a durable deferred
store). If the saga already left the waiting state, the timeout event has no matching transition
and is ignored.

## Compensation

Each transition can declare one or more `Compensate` commands with stable step ids. Successful
steps are recorded in `SagaProgress`. A transition marked `Failure()` runs the completed
compensations in reverse order **before** writing its target state, persisting progress after each
command. A failed compensation leaves the state unchanged, so retrying the failure event skips the
already-compensated steps; this is the restart-safe guarantee.

## Routing slips

A routing slip is a linear, predetermined sequence of activities with automatic reverse
compensation. Use it for "do these steps, undo the completed ones on failure"; use a saga when the
next step depends on an event.

```csharp
builder.Services.AddRoutingSlip<OrderArguments>(slip => slip
    .AddStep<ReserveStockActivity>("reserve")
    .AddStep<ChargeCardActivity>("charge", options => options.Timeout = TimeSpan.FromSeconds(30))
    .AddStep<ShipOrderActivity>("ship"));

public sealed class ReserveStockActivity(IInventory inventory) : IActivity<OrderArguments> {
    public Task ExecuteAsync(OrderArguments arguments, CancellationToken cancellationToken) {
        // side effect; record anything the compensation needs on the arguments
    }

    public Task CompensateAsync(OrderArguments arguments, CancellationToken cancellationToken) {
        // undo the side effect
    }
}
```

`AddRoutingSlip` registers the activities and the in-memory store; `AddCqrsEfRoutingSlips` from the
EF package replaces the store with a durable table. Arguments are serialized after every step, so
the slip resumes at the next step after a restart. A step failure compensates completed steps in
reverse order and ends `Compensated`; a failed compensation ends `Faulted` and records one dead
letter with both errors. Cancellation leaves the slip `Running` for a restart. Retry and timeout
are per step (and per compensation) through `RoutingSlipStepOptions`; inspect progress with
`IRoutingSlipStore.ListAsync`.

Metrics are `cqrs.routing_slip.active`, `.completed`, `.compensated`, `.faulted`, and `.step`.
`AddCqrsRoutingSlipHealthChecks()` reports running/compensating slips older than
`RoutingSlipHealthOptions.StuckAfter`.

## Observability

- `SagaDiagnosticNames.Active`, `.Completed`, `.Faulted` instruments (`cqrs.saga.*`) report
  persisted rows and terminal transitions.
- `SagaDiagnosticNames.ActivitySource` emits one `SagaStep` span per event with saga id, state
  type, message, state, and outcome tags.
- `AddCqrsSagaObserver<TObserver>()` adds a typed pre/transition/fault observer. Observer failures
  are isolated, logged, and counted; they never change the saga outcome.
- `AddCqrsSagaHealthChecks()` (from `Jeninnet.CQRS.HealthChecks`) reports active sagas older than
  `SagaHealthOptions.StuckAfter` as unhealthy; terminal states are ignored.

## Scope

No visual designer and no distributed saga coordination: each row is guarded by optimistic
concurrency, and v1 does not add cross-instance leases. The test harness and pluggable repository
seam remain CQRS-61.
