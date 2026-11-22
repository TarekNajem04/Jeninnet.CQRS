//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas;

/// <summary>
/// Read-only view of one saga event dispatch. The numeric status is null before the transition
/// resolves; a terminal transition carries the saved state and outcome.
/// </summary>
/// <param name="SagaId">The correlation id.</param>
/// <param name="StateType">The saga state type.</param>
/// <param name="MessageType">The event message type.</param>
/// <param name="State">The state after the transition, when known.</param>
/// <param name="Status">The transition outcome, when known.</param>
public sealed record SagaObservationContext(
    string SagaId,
    Type StateType,
    Type MessageType,
    string? State = null,
    SagaDispatchStatus? Status = null);

/// <summary>
/// Observes every saga event. Observers run in registration order before the transition, after a
/// resolved transition (including ignored events), and on a fault. An observer failure is logged
/// and counted but never changes the saga outcome.
/// </summary>
public interface ISagaObserver {
    /// <summary>Runs before the saga row is loaded.</summary>
    /// <param name="context">The saga and event identity.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    Task OnSagaEventAsync(SagaObservationContext context, CancellationToken cancellationToken = default);

    /// <summary>Runs after the transition is saved and follow-ups are dispatched.</summary>
    /// <param name="context">The final state and status.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    Task OnSagaTransitionAsync(SagaObservationContext context, CancellationToken cancellationToken = default);

    /// <summary>Runs when the transition or a follow-up fails.</summary>
    /// <param name="context">The saga and event identity.</param>
    /// <param name="error">The failure.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    Task OnSagaFaultAsync(SagaObservationContext context, Exception error, CancellationToken cancellationToken = default);
}
