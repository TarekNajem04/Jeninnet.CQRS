//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas;

/// <summary>
/// A small, explicit long-running process manager. Derive, set the initial state, and declare
/// transitions with <see cref="Correlate{TMessage}"/>; the machine is a data table over
/// <typeparamref name="TState"/>, and <see cref="SagaDispatcher"/> is the loop.
/// </summary>
/// <remarks>
/// <para>
/// Configuration is lazy: <see cref="Configure"/> runs on first use (or when the hosted
/// dispatcher starts), so a machine may read constructor-injected fields safely.
/// </para>
/// <para>
/// The same event message type may appear in only one machine or transition; the dispatcher
/// validates duplicates at startup. A transition applies to a new saga only when declared with
/// <c>Initially()</c>; an out-of-order event simply has no matching row in the table and is
/// ignored without writing.
/// </para>
/// </remarks>
/// <typeparam name="TState">The persisted saga state type.</typeparam>
/// <typeparam name="TEvent">The machine's event identifier enum.</typeparam>
public abstract class SagaStateMachine<TState, TEvent> : ISaga, ISagaRuntime
    where TState : class, ISagaState, new()
    where TEvent : struct, Enum {
    private readonly Dictionary<Type, SagaEventDefinition> _events = [];
    private readonly Dictionary<string, SagaCompensation> _compensations = new(StringComparer.Ordinal);
    private readonly HashSet<string> _terminalStates = new(StringComparer.Ordinal);
    private readonly Lock _sync = new();
    private bool _configured;
    private bool _configuring;
    private string _initialState = "Initial";

    /// <inheritdoc />
    public Type StateType => typeof(TState);

    /// <inheritdoc />
    public string InitialState => _initialState;

    /// <inheritdoc />
    public bool IsTerminal(string state) {
        EnsureConfigured();

        return !StringCompat.IsNullOrWhiteSpace(state) && _terminalStates.Contains(state);
    }

    /// <summary>Declares the machine's initial state and transition table.</summary>
    protected abstract void Configure();

    /// <summary>Sets the state assigned to a newly created saga.</summary>
    /// <param name="state">The initial state name.</param>
    protected void Initially(string state) {
        EnsureConfiguring();
        Guard.NotNullOrWhiteSpace(state);

        _initialState = state;
    }

    /// <summary>Declares a transition for <typeparamref name="TMessage"/>.</summary>
    /// <typeparam name="TMessage">The event message type.</typeparam>
    /// <param name="eventId">The machine's event identifier.</param>
    /// <param name="correlationId">Extracts the saga correlation id from the event.</param>
    /// <returns>The fluent transition builder.</returns>
    protected SagaEventBuilder<TState, TEvent, TMessage> Correlate<TMessage>(TEvent eventId, Func<TMessage, string> correlationId)
        where TMessage : class {
        EnsureConfiguring();
        Guard.NotNull(correlationId);

        var definition = new SagaEventDefinition<TState, TMessage>(eventId, correlationId);

        if(_events.ContainsKey(typeof(TMessage))) {
            throw new InvalidOperationException(
                $"The saga machine '{GetType().Name}' already correlates '{typeof(TMessage).FullName}'.");
        }

        _events.Add(typeof(TMessage), definition);

        return new SagaEventBuilder<TState, TEvent, TMessage>(definition);
    }

    void ISagaRuntime.EnsureConfigured() => EnsureConfigured();

    ISagaState ISagaRuntime.CreateState() => new TState { CurrentState = _initialState };

    IReadOnlyCollection<Type> ISagaRuntime.HandledMessageTypes => [.. _events.Keys];

    bool ISagaRuntime.TryGetEvent(Type messageType, out SagaEventDefinition definition) {
        if(_events.TryGetValue(messageType, out var found)) {
            definition = found;

            return true;
        }

        definition = null!;

        return false;
    }

    bool ISagaRuntime.TryGetCompensation(string stepId, out SagaCompensation compensation) =>
        _compensations.TryGetValue(stepId, out compensation!);

    internal void EnsureConfigured() {
        lock(_sync) {
            if(_configured) {
                return;
            }

            _configuring = true;

            try {
                Configure();
                BuildIndexes();
                ValidateTransitions();
                _configured = true;
            }
            catch {
                _events.Clear();
                _compensations.Clear();
                _terminalStates.Clear();
                _initialState = "Initial";
                throw;
            }
            finally {
                _configuring = false;
            }
        }
    }

    private void EnsureConfiguring() {
        if(!_configuring) {
            throw new InvalidOperationException(
                $"The saga machine '{GetType().Name}' must call Correlate/Initially from Configure(), not from a constructor or dispatch path.");
        }
    }

    private void BuildIndexes() {
        foreach(var definition in _events.Values) {
            if((definition.IsCompletion || definition.IsFailure) && definition.TargetState is not null) {
                _terminalStates.Add(definition.TargetState);
            }

            foreach(var compensation in definition.Compensations) {
                if(_compensations.ContainsKey(compensation.StepId)) {
                    throw new InvalidOperationException(
                        $"The saga machine '{GetType().Name}' declares the compensation step '{compensation.StepId}' more than once.");
                }

                _compensations.Add(compensation.StepId, compensation);
            }
        }
    }

    private void ValidateTransitions() {
        foreach(var definition in _events.Values) {
            if(!definition.RequiresNew && definition.SourceState is null) {
                throw new InvalidOperationException(
                    $"The saga transition for '{definition.MessageType.FullName}' must call Initially() or InState(...).");
            }

            if((definition.IsFailure || definition.IsCompletion) && definition.TargetState is null) {
                throw new InvalidOperationException(
                    $"The saga transition for '{definition.MessageType.FullName}' marks a terminal outcome but does not call TransitionTo(...).");
            }
        }
    }
}
