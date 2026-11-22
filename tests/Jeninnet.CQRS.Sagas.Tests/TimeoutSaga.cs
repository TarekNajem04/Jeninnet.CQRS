//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.Tests;

/// <summary>State for the timeout characterization saga.</summary>
public sealed class TimeoutSagaState : ISagaState {
    /// <inheritdoc />
    public string CurrentState { get; set; } = string.Empty;

    /// <summary>Gets or sets the correlation id.</summary>
    public string Id { get; set; } = string.Empty;
}

/// <summary>Starts the timeout saga.</summary>
/// <param name="Id">The correlation id.</param>
public sealed record TimeoutStarted(string Id) : INotification;

/// <summary>The scheduled timeout event.</summary>
/// <param name="Id">The correlation id.</param>
public sealed record TimeoutElapsed(string Id) : INotification;

/// <summary>A saga that waits for an event and fails over to a timed-out state.</summary>
public sealed class TimeoutSaga : SagaStateMachine<TimeoutSagaState, TimeoutEvent> {
    /// <inheritdoc />
    protected override void Configure() {
        Initially("Waiting");

        Correlate<TimeoutStarted>(TimeoutEvent.Started, static message => message.Id)
            .Initially()
            .Then(static (state, message) => state.Id = message.Id)
            .TransitionTo("Waiting")
            .OnTimeout(TimeSpan.FromMinutes(5), static state => new TimeoutElapsed(state.Id));

        Correlate<TimeoutElapsed>(TimeoutEvent.Elapsed, static message => message.Id)
            .InState("Waiting")
            .TransitionTo("TimedOut")
            .Completed();
    }
}

/// <summary>Event identifiers for the timeout saga.</summary>
public enum TimeoutEvent {
    /// <summary>The saga started.</summary>
    Started = 0,

    /// <summary>The timeout elapsed.</summary>
    Elapsed = 1,
}
