//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.EntityFrameworkCore.Tests;

/// <summary>State for the compensation saga.</summary>
public sealed class CompensationSagaState : ISagaState {
    /// <inheritdoc />
    public string CurrentState { get; set; } = string.Empty;

    /// <summary>Gets or sets the correlation id.</summary>
    public string Id { get; set; } = string.Empty;
}

/// <summary>Event identifiers for the compensation saga.</summary>
public enum CompensationEvent {
    /// <summary>The saga started and recorded two compensable steps.</summary>
    Started = 0,

    /// <summary>A downstream step failed.</summary>
    Failed = 1,
}

/// <summary>Starts the compensation saga.</summary>
/// <param name="Id">The correlation id.</param>
public sealed record CompensationStarted(string Id) : INotification;

/// <summary>Signals that compensation should run.</summary>
/// <param name="Id">The correlation id.</param>
public sealed record CompensationFailed(string Id) : INotification;

/// <summary>A compensation command dispatched in reverse step order.</summary>
/// <param name="Name">The step name.</param>
public sealed record CompensationCommand(string Name) : ICommand;

/// <summary>
/// Two completed steps followed by a failure event. The failure transition runs the compensation
/// stack in reverse; progress is persisted after each successful undo.
/// </summary>
public sealed class CompensatingSaga : SagaStateMachine<CompensationSagaState, CompensationEvent> {
    /// <inheritdoc />
    protected override void Configure() {
        Initially("Awaiting");

        Correlate<CompensationStarted>(CompensationEvent.Started, static message => message.Id)
            .Initially()
            .Then(static (state, message) => state.Id = message.Id)
            .TransitionTo("Awaiting")
            .Compensate("step-1", static _ => new CompensationCommand("one"))
            .Compensate("step-2", static _ => new CompensationCommand("two"));

        Correlate<CompensationFailed>(CompensationEvent.Failed, static message => message.Id)
            .InState("Awaiting")
            .Failure()
            .TransitionTo("Faulted");
    }
}