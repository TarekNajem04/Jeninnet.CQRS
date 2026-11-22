//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas;

/// <summary>A follow-up command deferred after the saga transition commits.</summary>
internal sealed record SagaScheduledCommand(
    Type MessageType,
    TimeSpan Delay,
    Func<IServiceProvider, CancellationToken, Task> Dispatch);

/// <summary>One declared compensation for a completed step, addressable by a stable id.</summary>
internal sealed record SagaCompensation(
    string StepId,
    Type MessageType,
    Func<ISagaState, CascadeStep> CreateStep);

/// <summary>
/// One row in a machine's transition table. The non-generic shape lets the executor loop over
/// machines without reflection; the typed subclass closes the event message type once.
/// </summary>
internal abstract class SagaEventDefinition {
    protected SagaEventDefinition(Type messageType, object eventId) {
        MessageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
        EventId = eventId ?? throw new ArgumentNullException(nameof(eventId));
    }

    /// <summary>Gets the event message type.</summary>
    internal Type MessageType { get; }

    /// <summary>Gets the machine's event identifier (the enum value boxed).</summary>
    internal object EventId { get; }

    /// <summary>Gets or sets whether the transition applies only while creating a new saga.</summary>
    internal bool RequiresNew { get; set; }

    /// <summary>Gets or sets the source state for a non-new transition.</summary>
    internal string? SourceState { get; set; }

    /// <summary>Gets or sets the state written after the transition; null keeps the current state.</summary>
    internal string? TargetState { get; set; }

    /// <summary>Gets or sets whether handling this event triggers the compensation stack.</summary>
    internal bool IsFailure { get; set; }

    /// <summary>Gets or sets whether the target state is a normal completion.</summary>
    internal bool IsCompletion { get; set; }

    /// <summary>Gets the compensations this step contributes once completed.</summary>
    internal List<SagaCompensation> Compensations { get; } = [];

    /// <summary>Extracts the correlation id from the event message.</summary>
    internal abstract string Correlate(object message);

    /// <summary>Returns whether this transition applies to the current saga position.</summary>
    internal bool Matches(string currentState, bool isNew) =>
        RequiresNew
            ? isNew
            : !isNew && SourceState is not null && string.Equals(currentState, SourceState, StringComparison.Ordinal);

    /// <summary>Applies the in-memory state effects.</summary>
    internal abstract void ApplyEffects(object state, object message);

    /// <summary>Builds the typed follow-up closures for this transition.</summary>
    internal abstract void CollectFollowUps(object state, object message, List<CascadeStep> steps);

    /// <summary>Builds the deferred follow-up closures for this transition.</summary>
    internal abstract void CollectSchedules(object state, object message, List<SagaScheduledCommand> schedules);

    /// <summary>Builds the typed compensation closures for this step.</summary>
    internal abstract void CollectCompensations(ISagaState state, List<(string StepId, CascadeStep Step)> steps);
}

/// <summary>Typed transition definition; the event type is closed once at configuration time.</summary>
internal sealed class SagaEventDefinition<TState, TMessage> : SagaEventDefinition
    where TState : class, ISagaState
    where TMessage : class {
    private readonly Func<TMessage, string> _correlate;

    internal SagaEventDefinition(object eventId, Func<TMessage, string> correlate)
        : base(typeof(TMessage), eventId) {
        _correlate = correlate ?? throw new ArgumentNullException(nameof(correlate));
    }

    internal List<Action<TState, TMessage>> Effects { get; } = [];

    internal List<Func<TState, TMessage, CascadeStep>> FollowUps { get; } = [];

    internal List<Func<TState, TMessage, SagaScheduledCommand>> Schedules { get; } = [];

    internal override string Correlate(object message) => _correlate((TMessage)message);

    internal override void ApplyEffects(object state, object message) {
        var typedState = (TState)state;
        var typedMessage = (TMessage)message;

        foreach(var effect in Effects) {
            effect(typedState, typedMessage);
        }
    }

    internal override void CollectFollowUps(object state, object message, List<CascadeStep> steps) {
        var typedState = (TState)state;
        var typedMessage = (TMessage)message;

        foreach(var factory in FollowUps) {
            steps.Add(factory(typedState, typedMessage));
        }
    }

    internal override void CollectSchedules(object state, object message, List<SagaScheduledCommand> schedules) {
        var typedState = (TState)state;
        var typedMessage = (TMessage)message;

        foreach(var factory in Schedules) {
            schedules.Add(factory(typedState, typedMessage));
        }
    }

    internal override void CollectCompensations(ISagaState state, List<(string StepId, CascadeStep Step)> steps) {
        foreach(var compensation in Compensations) {
            steps.Add((compensation.StepId, compensation.CreateStep(state)));
        }
    }
}

/// <summary>
/// Runtime seam used by <see cref="SagaDispatcher"/> after lazy configuration. It is internal so
/// the public surface stays the declarative machine; every built-in machine implements it.
/// </summary>
internal interface ISagaRuntime {
    Type StateType { get; }

    string InitialState { get; }

    IReadOnlyCollection<Type> HandledMessageTypes { get; }

    bool IsTerminal(string state);

    void EnsureConfigured();

    ISagaState CreateState();

    bool TryGetEvent(Type messageType, out SagaEventDefinition definition);

    bool TryGetCompensation(string stepId, out SagaCompensation compensation);
}
