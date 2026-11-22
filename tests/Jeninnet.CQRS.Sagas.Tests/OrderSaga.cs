//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.Tests;

/// <summary>The saga's persisted data. The executor writes only CurrentState.</summary>
public sealed class OrderSagaState : ISagaState {
    /// <inheritdoc />
    public string CurrentState { get; set; } = string.Empty;

    /// <summary>Gets or sets the order correlation value.</summary>
    public string OrderId { get; set; } = string.Empty;

    /// <summary>Gets or sets the captured payment id.</summary>
    public string PaymentId { get; set; } = string.Empty;
}

/// <summary>The machine's event identifiers.</summary>
public enum OrderEvent {
    /// <summary>An order was placed.</summary>
    Placed = 0,

    /// <summary>Payment was taken.</summary>
    PaymentTaken = 1,
}

/// <summary>The first event in the two-event saga.</summary>
/// <param name="OrderId">The order correlation id.</param>
public sealed record OrderPlaced(string OrderId) : INotification;

/// <summary>The second event in the two-event saga.</summary>
/// <param name="OrderId">The order correlation id.</param>
/// <param name="PaymentId">The payment id.</param>
public sealed record PaymentTaken(string OrderId, string PaymentId) : INotification;

/// <summary>A follow-up command published from the initial transition.</summary>
/// <param name="OrderId">The order correlation id.</param>
public sealed record ReserveStock(string OrderId) : ICommand;

/// <summary>A follow-up command published from the payment transition.</summary>
/// <param name="OrderId">The order correlation id.</param>
public sealed record ShipOrder(string OrderId) : ICommand;

/// <summary>The two-event order saga used by the characterization tests.</summary>
public sealed class OrderSaga : SagaStateMachine<OrderSagaState, OrderEvent> {
    /// <inheritdoc />
    protected override void Configure() {
        Initially("AwaitingPayment");

        Correlate<OrderPlaced>(OrderEvent.Placed, static message => message.OrderId)
            .Initially()
            .Then(static (state, message) => state.OrderId = message.OrderId)
            .TransitionTo("AwaitingPayment")
            .Publish(static (state, _) => new ReserveStock(state.OrderId));

        Correlate<PaymentTaken>(OrderEvent.PaymentTaken, static message => message.OrderId)
            .InState("AwaitingPayment")
            .Then(static (state, message) => state.PaymentId = message.PaymentId)
            .TransitionTo("Completed")
            .Completed()
            .Publish(static (state, _) => new ShipOrder(state.OrderId));
    }
}