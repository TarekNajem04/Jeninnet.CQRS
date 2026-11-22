//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas;

/// <summary>
/// Fluent transition builder returned by <see cref="SagaStateMachine{TState,TEvent}.Correlate{TMessage}"/>.
/// Methods mutate one transition definition and return the builder, so a machine reads as a table.
/// </summary>
/// <typeparam name="TState">The persisted state type.</typeparam>
/// <typeparam name="TEvent">The machine's event enum.</typeparam>
/// <typeparam name="TMessage">The event message type.</typeparam>
public sealed class SagaEventBuilder<TState, TEvent, TMessage> // NOSONAR: Generic adapter/state-machine arity is part of the public contract.
    where TState : class, ISagaState
    where TEvent : struct, Enum
    where TMessage : class {
    private readonly SagaEventDefinition<TState, TMessage> _definition;

    internal SagaEventBuilder(SagaEventDefinition<TState, TMessage> definition) =>
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));

    /// <summary>Marks the transition as applying only when the saga is created by this event.</summary>
    public SagaEventBuilder<TState, TEvent, TMessage> Initially() {
        _definition.RequiresNew = true;
        _definition.SourceState = null;

        return this;
    }

    /// <summary>Marks the transition as applying only while the saga is in <paramref name="state"/>.</summary>
    /// <param name="state">The source state name.</param>
    public SagaEventBuilder<TState, TEvent, TMessage> InState(string state) {
        Guard.NotNullOrWhiteSpace(state);

        _definition.RequiresNew = false;
        _definition.SourceState = state;

        return this;
    }

    /// <summary>Adds an in-memory state mutation, applied in registration order.</summary>
    /// <param name="effect">The effect.</param>
    public SagaEventBuilder<TState, TEvent, TMessage> Then(Action<TState, TMessage> effect) {
        Guard.NotNull(effect);

        _definition.Effects.Add(effect);

        return this;
    }

    /// <summary>Sets the state written after the effects; omit it to keep the current state.</summary>
    /// <param name="state">The target state name.</param>
    public SagaEventBuilder<TState, TEvent, TMessage> TransitionTo(string state) {
        Guard.NotNullOrWhiteSpace(state);

        _definition.TargetState = state;

        return this;
    }

    /// <summary>Marks the transition as a normal completion; its target state becomes terminal.</summary>
    public SagaEventBuilder<TState, TEvent, TMessage> Completed() {
        _definition.IsCompletion = true;

        return this;
    }

    /// <summary>Marks a failure event: its compensations run in reverse before the target state is written.</summary>
    public SagaEventBuilder<TState, TEvent, TMessage> Failure() {
        _definition.IsFailure = true;

        return this;
    }

    /// <summary>Queues a void command to dispatch after the state transition commits.</summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <param name="command">Builds the command from the current state and event.</param>
    public SagaEventBuilder<TState, TEvent, TMessage> Publish<TCommand>(Func<TState, TMessage, TCommand> command)
        where TCommand : ICommand {
        Guard.NotNull(command);

        _definition.FollowUps.Add((state, message) => {
            var instance = command(state, message) ?? throw new InvalidOperationException(
                $"The saga '{typeof(TState).Name}' produced a null {typeof(TCommand).Name} command.");

            return new CascadeStep(typeof(TCommand), (broker, cancellationToken) => broker.DispatchAsync(instance, cancellationToken));
        });

        return this;
    }

    /// <summary>Queues a result-producing command to dispatch after the state transition commits.</summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <typeparam name="TResult">The discarded command result type.</typeparam>
    /// <param name="command">Builds the command from the current state and event.</param>
    public SagaEventBuilder<TState, TEvent, TMessage> Publish<TCommand, TResult>(Func<TState, TMessage, TCommand> command)
        where TCommand : ICommand<TResult> {
        Guard.NotNull(command);

        _definition.FollowUps.Add((state, message) => {
            var instance = command(state, message) ?? throw new InvalidOperationException(
                $"The saga '{typeof(TState).Name}' produced a null {typeof(TCommand).Name} command.");

            return new CascadeStep(typeof(TCommand), async (broker, cancellationToken) =>
                await broker.DispatchAsync<TCommand, TResult>(instance, cancellationToken).ConfigureAwait(false));
        });

        return this;
    }

    /// <summary>Queues a notification to publish after the state transition commits.</summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <param name="notification">Builds the notification from the current state and event.</param>
    public SagaEventBuilder<TState, TEvent, TMessage> PublishNotification<TNotification>(Func<TState, TMessage, TNotification> notification)
        where TNotification : INotification {
        Guard.NotNull(notification);

        _definition.FollowUps.Add((state, message) => {
            var instance = notification(state, message) ?? throw new InvalidOperationException(
                $"The saga '{typeof(TState).Name}' produced a null {typeof(TNotification).Name} notification.");

            return new CascadeStep(typeof(TNotification), (broker, cancellationToken) => broker.PublishAsync(instance, cancellationToken));
        });

        return this;
    }

    /// <summary>Alias for <see cref="Publish{TCommand}(Func{TState,TMessage,TCommand})"/>.</summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <param name="command">Builds the command from the current state and event.</param>
    public SagaEventBuilder<TState, TEvent, TMessage> Send<TCommand>(Func<TState, TMessage, TCommand> command)
        where TCommand : ICommand => Publish(command);

    /// <summary>Alias for the result-producing command overload.</summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <typeparam name="TResult">The discarded command result type.</typeparam>
    /// <param name="command">Builds the command from the current state and event.</param>
    public SagaEventBuilder<TState, TEvent, TMessage> Send<TCommand, TResult>(Func<TState, TMessage, TCommand> command)
        where TCommand : ICommand<TResult> => Publish<TCommand, TResult>(command);

    /// <summary>Declares the undo command for this transition; completed steps compensate in reverse.</summary>
    /// <typeparam name="TCommand">The compensation command type.</typeparam>
    /// <param name="command">Builds the compensation command from the current state.</param>
    public SagaEventBuilder<TState, TEvent, TMessage> Compensate<TCommand>(Func<TState, TCommand> command)
        where TCommand : ICommand =>
        Compensate($"{typeof(TMessage).FullName}:{_definition.Compensations.Count}", command);

    /// <summary>Declares a compensation with an explicit stable step id.</summary>
    /// <typeparam name="TCommand">The compensation command type.</typeparam>
    /// <param name="stepId">Stable id persisted in the progress log.</param>
    /// <param name="command">Builds the compensation command from the current state.</param>
    public SagaEventBuilder<TState, TEvent, TMessage> Compensate<TCommand>(string stepId, Func<TState, TCommand> command)
        where TCommand : ICommand {
        Guard.NotNullOrWhiteSpace(stepId);
        Guard.NotNull(command);

        _definition.Compensations.Add(new SagaCompensation(stepId, typeof(TCommand), state => {
            var instance = command((TState)state) ?? throw new InvalidOperationException(
                $"The saga '{typeof(TState).Name}' produced a null compensation {typeof(TCommand).Name} command.");

            return new CascadeStep(typeof(TCommand), (broker, cancellationToken) => broker.DispatchAsync(instance, cancellationToken));
        }));

        return this;
    }

    /// <summary>Declares a result-producing compensation command.</summary>
    /// <typeparam name="TCommand">The compensation command type.</typeparam>
    /// <typeparam name="TResult">The discarded result type.</typeparam>
    /// <param name="command">Builds the compensation command from the current state.</param>
    public SagaEventBuilder<TState, TEvent, TMessage> Compensate<TCommand, TResult>(Func<TState, TCommand> command)
        where TCommand : ICommand<TResult> =>
        Compensate<TCommand, TResult>($"{typeof(TMessage).FullName}:{_definition.Compensations.Count}", command);

    /// <summary>Declares a result-producing compensation with an explicit stable step id.</summary>
    /// <typeparam name="TCommand">The compensation command type.</typeparam>
    /// <typeparam name="TResult">The discarded result type.</typeparam>
    /// <param name="stepId">Stable id persisted in the progress log.</param>
    /// <param name="command">Builds the compensation command from the current state.</param>
    public SagaEventBuilder<TState, TEvent, TMessage> Compensate<TCommand, TResult>(string stepId, Func<TState, TCommand> command)
        where TCommand : ICommand<TResult> {
        Guard.NotNullOrWhiteSpace(stepId);
        Guard.NotNull(command);

        _definition.Compensations.Add(new SagaCompensation(stepId, typeof(TCommand), state => {
            var instance = command((TState)state) ?? throw new InvalidOperationException(
                $"The saga '{typeof(TState).Name}' produced a null compensation {typeof(TCommand).Name} command.");

            return new CascadeStep(typeof(TCommand), async (broker, cancellationToken) =>
                await broker.DispatchAsync<TCommand, TResult>(instance, cancellationToken).ConfigureAwait(false));
        }));

        return this;
    }

    /// <summary>Declares a notification compensation with an explicit stable step id.</summary>
    /// <typeparam name="TNotification">The compensation notification type.</typeparam>
    /// <param name="stepId">Stable id persisted in the progress log.</param>
    /// <param name="notification">Builds the compensation notification from the current state.</param>
    public SagaEventBuilder<TState, TEvent, TMessage> CompensateNotification<TNotification>(string stepId, Func<TState, TNotification> notification)
        where TNotification : INotification {
        Guard.NotNullOrWhiteSpace(stepId);
        Guard.NotNull(notification);

        _definition.Compensations.Add(new SagaCompensation(stepId, typeof(TNotification), state => {
            var instance = notification((TState)state) ?? throw new InvalidOperationException(
                $"The saga '{typeof(TState).Name}' produced a null compensation {typeof(TNotification).Name} notification.");

            return new CascadeStep(typeof(TNotification), (broker, cancellationToken) => broker.PublishAsync(instance, cancellationToken));
        }));

        return this;
    }

    /// <summary>Schedules a timeout event through the deferred scheduler after this transition.</summary>
    /// <typeparam name="TTimeout">The timeout notification type.</typeparam>
    /// <param name="timeout">How long to wait before dispatching the event.</param>
    /// <param name="eventFactory">Builds the timeout event from the current state.</param>
    public SagaEventBuilder<TState, TEvent, TMessage> Schedule<TTimeout>(TimeSpan timeout, Func<TState, TTimeout> eventFactory)
        where TTimeout : INotification {
        Guard.NotNull(eventFactory);

        _definition.Schedules.Add((state, _) => new SagaScheduledCommand(
            typeof(TTimeout),
            timeout,
            (provider, cancellationToken) => {
                var timeoutEvent = eventFactory(state) ?? throw new InvalidOperationException(
                    $"The saga '{typeof(TState).Name}' produced a null {typeof(TTimeout).Name} timeout event.");
                var serializer = provider.GetRequiredService<IMessageSerializer>();
                var payload = serializer.Serialize(timeoutEvent, timeoutEvent.GetType());
                var command = new SagaTimeoutCommand(
                    typeof(TTimeout).AssemblyQualifiedName ?? typeof(TTimeout).FullName ?? typeof(TTimeout).Name,
                    serializer.ContentType,
                    payload);

                return provider.GetRequiredService<IDeferredDispatcher>().DeferAsync(command, timeout, cancellationToken);
            }));

        return this;
    }

    /// <summary>Alias for <see cref="Schedule{TTimeout}(TimeSpan, Func{TState,TTimeout})"/>.</summary>
    /// <typeparam name="TTimeout">The timeout notification type.</typeparam>
    /// <param name="timeout">How long to wait before dispatching the event.</param>
    /// <param name="eventFactory">Builds the timeout event from the current state.</param>
    public SagaEventBuilder<TState, TEvent, TMessage> OnTimeout<TTimeout>(TimeSpan timeout, Func<TState, TTimeout> eventFactory)
        where TTimeout : INotification => Schedule(timeout, eventFactory);

    /// <summary>Defers a command after the state transition commits.</summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <param name="delay">How long after the transition to run the command.</param>
    /// <param name="command">Builds the command from the current state and event.</param>
    public SagaEventBuilder<TState, TEvent, TMessage> Schedule<TCommand>(TimeSpan delay, Func<TState, TMessage, TCommand> command)
        where TCommand : ICommand {
        Guard.NotNull(command);

        _definition.Schedules.Add((state, message) => {
            var instance = command(state, message) ?? throw new InvalidOperationException(
                $"The saga '{typeof(TState).Name}' produced a null scheduled {typeof(TCommand).Name} command.");

            return new SagaScheduledCommand(
                typeof(TCommand),
                delay,
                (provider, cancellationToken) => provider.GetRequiredService<IDeferredDispatcher>().DeferAsync(instance, delay, cancellationToken));
        });

        return this;
    }

    /// <summary>Defers a result-producing command after the state transition commits.</summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <typeparam name="TResult">The discarded command result type.</typeparam>
    /// <param name="delay">How long after the transition to run the command.</param>
    /// <param name="command">Builds the command from the current state and event.</param>
    public SagaEventBuilder<TState, TEvent, TMessage> Schedule<TCommand, TResult>(TimeSpan delay, Func<TState, TMessage, TCommand> command)
        where TCommand : ICommand<TResult> {
        Guard.NotNull(command);

        _definition.Schedules.Add((state, message) => {
            var instance = command(state, message) ?? throw new InvalidOperationException(
                $"The saga '{typeof(TState).Name}' produced a null scheduled {typeof(TCommand).Name} command.");

            return new SagaScheduledCommand(
                typeof(TCommand),
                delay,
                (provider, cancellationToken) => provider.GetRequiredService<IDeferredDispatcher>()
                    .DeferAsync<TCommand, TResult>(instance, delay, cancellationToken));
        });

        return this;
    }
}
