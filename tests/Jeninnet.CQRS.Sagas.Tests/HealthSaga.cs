//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.Tests;

/// <summary>State for the health-check saga.</summary>
public sealed class HealthSagaState : ISagaState {
    /// <inheritdoc />
    public string CurrentState { get; set; } = string.Empty;

    /// <summary>Gets or sets the correlation id.</summary>
    public string Id { get; set; } = string.Empty;
}

/// <summary>Starts the health-check saga.</summary>
/// <param name="Id">The correlation id.</param>
public sealed record HealthStarted(string Id) : INotification;

/// <summary>Completes the health-check saga.</summary>
/// <param name="Id">The correlation id.</param>
public sealed record HealthFinished(string Id) : INotification;

/// <summary>A saga with no follow-ups, used to isolate the stuck-health contribution.</summary>
public sealed class HealthSaga : SagaStateMachine<HealthSagaState, HealthEvent> {
    /// <inheritdoc />
    protected override void Configure() {
        Initially("Waiting");

        Correlate<HealthStarted>(HealthEvent.Started, static message => message.Id)
            .Initially()
            .Then(static (state, message) => state.Id = message.Id)
            .TransitionTo("Waiting");

        Correlate<HealthFinished>(HealthEvent.Finished, static message => message.Id)
            .InState("Waiting")
            .TransitionTo("Completed")
            .Completed();
    }
}

/// <summary>Event identifiers for the health saga.</summary>
public enum HealthEvent {
    /// <summary>The saga started.</summary>
    Started = 0,

    /// <summary>The saga finished.</summary>
    Finished = 1,
}