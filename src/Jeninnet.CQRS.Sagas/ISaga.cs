//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas;

/// <summary>
/// The persisted data of one long-running process manager. Implementations add the saga's own
/// fields; <see cref="CurrentState"/> is the transition-table position and must be settable so
/// the executor can move the saga without reflection.
/// </summary>
public interface ISagaState {
    /// <summary>Gets or sets the current state name used by the transition table.</summary>
    string CurrentState { get; set; }
}

/// <summary>
/// Non-generic view of a saga machine used by the dispatcher and diagnostics. Derive from
/// <see cref="SagaStateMachine{TState,TEvent}"/>; the runtime details stay internal.
/// </summary>
public interface ISaga {
    /// <summary>Gets the concrete state type persisted for this machine.</summary>
    Type StateType { get; }

    /// <summary>Gets the state assigned to a new saga.</summary>
    string InitialState { get; }

    /// <summary>Gets whether <paramref name="state"/> is a terminal state (completed or faulted).</summary>
    /// <param name="state">The state name to test.</param>
    bool IsTerminal(string state);
}
