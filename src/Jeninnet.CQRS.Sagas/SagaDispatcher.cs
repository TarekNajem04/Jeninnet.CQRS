//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas;

/// <summary>
/// Routes notifications into saga machines: correlate, load, apply the transition table, save
/// with optimistic concurrency, then dispatch follow-ups. No reflection is used; each machine
/// closes its event message types in its own configuration.
/// </summary>
/// <remarks>
/// The observer runs synchronously inside the notification publish. It creates a fresh DI scope
/// per attempt, so saga state, command handlers, and follow-up dispatch never share a stale
/// context. A version conflict reloads and re-evaluates the transition, bounded by
/// <see cref="SagaOptions.MaxConcurrencyRetries"/>. Follow-ups are dispatched after the save; a
/// failure there surfaces to the publisher, but the state is already durable (at-least-once
/// boundary).
/// </remarks>
public sealed class SagaDispatcher : INotificationObserver {
    private readonly IServiceScopeFactory _scopes;
    private readonly ISagaRuntime[] _machines;
    private readonly int _maxRetries;
    private readonly SagaMetrics _metrics;
    private readonly SagaObserverDispatcher? _observers;
    private readonly Lock _sync = new();
    private bool _configured;

    /// <summary>Initializes the dispatcher.</summary>
    /// <param name="scopes">Creates one scope per event attempt. Never <see langword="null"/>.</param>
    /// <param name="machines">Registered machines. Never <see langword="null"/>.</param>
    /// <param name="options">Retry policy. Never <see langword="null"/>.</param>
    /// <param name="metrics">Saga instruments. Never <see langword="null"/>.</param>
    /// <param name="observers">Saga lifecycle observers; <see langword="null"/> when observation is not configured.</param>
    public SagaDispatcher(
        IServiceScopeFactory scopes,
        IEnumerable<ISaga> machines,
        IOptions<SagaOptions> options,
        SagaMetrics metrics,
        SagaObserverDispatcher? observers = null) {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _observers = observers;

        _machines = (machines ?? throw new ArgumentNullException(nameof(machines)))
            .GroupBy(static machine => machine.GetType())
            .Select(static group => group.First())
            .Select(static machine => machine as ISagaRuntime ?? throw new InvalidOperationException(
                $"'{machine.GetType().FullName}' implements ISaga but does not derive from {typeof(SagaStateMachine<,>).Name}."))
            .ToArray();

        var retries = (options ?? throw new ArgumentNullException(nameof(options))).Value.MaxConcurrencyRetries;

        if(retries < 0) {
            throw new ArgumentOutOfRangeException(nameof(options), retries, "MaxConcurrencyRetries must not be negative.");
        }

        _maxRetries = retries;
    }

    /// <summary>Configures every machine and rejects duplicate event ownership.</summary>
    /// <exception cref="InvalidOperationException">Two machines correlate the same event type.</exception>
    public void EnsureConfigured() {
        lock(_sync) {
            if(_configured) {
                return;
            }

            var owners = new Dictionary<Type, Type>();

            foreach(var machine in _machines) {
                machine.EnsureConfigured();

                foreach(var messageType in machine.HandledMessageTypes) {
                    if(owners.TryGetValue(messageType, out var existing)) {
                        throw new InvalidOperationException(
                            $"Saga event '{messageType.FullName}' is handled by both '{existing.Name}' and '{machine.GetType().Name}'.");
                    }

                    owners.Add(messageType, machine.GetType());
                }
            }

            _configured = true;
        }
    }

    /// <inheritdoc />
    public async Task OnPublishingAsync(object notification, Type notificationType, CancellationToken cancellationToken = default) =>
        await DispatchAsync(notification, notificationType, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Resolves a serialized event type name against the configured machines without reflection.
    /// Used by <see cref="SagaTimeoutCommandHandler"/> after a restart.
    /// </summary>
    /// <param name="typeName">Assembly-qualified, full, or simple event type name.</param>
    public Type? ResolveEventType(string typeName) {
        Guard.NotNullOrWhiteSpace(typeName);
        EnsureConfigured();

        foreach(var machine in _machines) {
            foreach(var candidate in machine.HandledMessageTypes) { // NOSONAR: Explicit loop is clearer and faster than a LINQ pipeline here.
                if(string.Equals(candidate.AssemblyQualifiedName, typeName, StringComparison.Ordinal) ||
                   string.Equals(candidate.FullName, typeName, StringComparison.Ordinal) ||
                   string.Equals(candidate.Name, typeName, StringComparison.Ordinal)) {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Dispatches one event. Returns <see langword="null"/> when no machine owns the message type;
    /// returns an ignored result when the correlation id is unknown for a non-initial event or the
    /// current state has no matching transition.
    /// </summary>
    /// <param name="message">The event message.</param>
    /// <param name="messageType">The runtime event type.</param>
    /// <param name="cancellationToken">Cancels the read, save, and follow-ups.</param>
    public async Task<SagaDispatchResult?> DispatchAsync(object message, Type messageType, CancellationToken cancellationToken = default) { // NOSONAR: Complexity is inherent to this state machine/pipeline and is covered by tests.
        Guard.NotNull(message);
        Guard.NotNull(messageType);
        EnsureConfigured();

        using var activity = SagaDiagnosticNames.ActivitySource.StartActivity(SagaDiagnosticNames.StepActivity);
        activity?.SetTag(SagaDiagnosticNames.MessageTag, messageType.Name);

        ISagaRuntime? machine = null;
        SagaEventDefinition? definition = null;

        foreach(var candidate in _machines) {
            if(candidate.TryGetEvent(messageType, out var found)) {
                machine = candidate;
                definition = found;
                break;
            }
        }

        if(machine is null || definition is null) {
            activity?.SetTag(SagaDiagnosticNames.OutcomeTag, "not-handled");

            return null;
        }

        activity?.SetTag(SagaDiagnosticNames.StateTypeTag, machine.StateType.Name);

        var sagaId = definition.Correlate(message);

        if(StringCompat.IsNullOrWhiteSpace(sagaId)) {
            throw new SagaCorrelationException(messageType);
        }

        activity?.SetTag(SagaDiagnosticNames.SagaIdTag, sagaId);

        var observation = new SagaObservationContext(sagaId, machine.StateType, messageType);
        await ObserveEventAsync(observation, cancellationToken).ConfigureAwait(false);
        SagaConcurrencyException? lastConflict = null;

        try {
            for(var attempt = 0; attempt <= _maxRetries; attempt++) {
                using var scope = _scopes.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<ISagaStore>();
                var record = await store.LoadAsync(sagaId, machine.StateType, cancellationToken).ConfigureAwait(false);

                ISagaState state;
                long expectedVersion;
                SagaDispatchStatus status;
                SagaProgress progress;

                if(record is null) {
                    if(!definition.RequiresNew) {
                        activity?.SetTag(SagaDiagnosticNames.OutcomeTag, "ignored");
                        activity?.SetTag(SagaDiagnosticNames.StateTag, string.Empty);

                        var ignored = new SagaDispatchResult(sagaId, SagaDispatchStatus.Ignored, string.Empty, [], []);
                        await ObserveTransitionAsync(observation with { State = ignored.State, Status = ignored.Status }, cancellationToken)
                            .ConfigureAwait(false);

                        return ignored;
                    }

                    state = machine.CreateState();
                    expectedVersion = 0;
                    status = SagaDispatchStatus.Created;
                    progress = SagaProgress.Empty;
                } else {
                    if(!definition.Matches(record.State.CurrentState, isNew: false)) {
                        activity?.SetTag(SagaDiagnosticNames.OutcomeTag, "ignored");
                        activity?.SetTag(SagaDiagnosticNames.StateTag, record.State.CurrentState);

                        var ignored = new SagaDispatchResult(sagaId, SagaDispatchStatus.Ignored, record.State.CurrentState, [], []);
                        await ObserveTransitionAsync(observation with { State = ignored.State, Status = ignored.Status }, cancellationToken)
                            .ConfigureAwait(false);

                        return ignored;
                    }

                    state = record.State;
                    expectedVersion = record.Version;
                    status = SagaDispatchStatus.Transitioned;
                    progress = record.ProgressState;
                }

                if(definition.IsFailure) {
                    (progress, expectedVersion) = await RunCompensationsAsync(
                        machine,
                        store,
                        scope.ServiceProvider,
                        sagaId,
                        state,
                        progress,
                        expectedVersion,
                        cancellationToken).ConfigureAwait(false);
                }

                definition.ApplyEffects(state, message);

                if(definition.TargetState is not null) {
                    state.CurrentState = definition.TargetState;
                }

                if(!definition.IsFailure) {
                    progress = AddCompletedSteps(progress, definition);
                }

                try {
                    await store.SaveAsync(sagaId, machine.StateType, state, expectedVersion, progress, cancellationToken).ConfigureAwait(false);
                }
                catch(SagaConcurrencyException exception) when(attempt < _maxRetries) {
                    lastConflict = exception;

                    continue;
                }

                if(definition.IsCompletion) {
                    _metrics.RecordCompleted(machine.StateType, state.CurrentState);
                }

                if(definition.IsFailure) {
                    _metrics.RecordFaulted(machine.StateType, state.CurrentState);
                }

                activity?.SetTag(SagaDiagnosticNames.StateTag, state.CurrentState);
                activity?.SetTag(SagaDiagnosticNames.OutcomeTag, status is SagaDispatchStatus.Created ? "created" : "transitioned");

                var steps = new List<CascadeStep>();
                definition.CollectFollowUps(state, message, steps);

                var published = new List<Type>(steps.Count);
                var broker = steps.Count is 0
                    ? null
                    : scope.ServiceProvider.GetRequiredService<ICqrsBroker>();

                foreach(var step in steps) {
                    await step.Dispatch(broker!, cancellationToken).ConfigureAwait(false);
                    published.Add(step.MessageType);
                }

                var schedules = new List<SagaScheduledCommand>();
                definition.CollectSchedules(state, message, schedules);
                var scheduled = new List<Type>(schedules.Count);

                if(schedules.Count > 0) {
                    foreach(var schedule in schedules) {
                        await schedule.Dispatch(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
                        scheduled.Add(schedule.MessageType);
                    }
                }

                var result = new SagaDispatchResult(
                    sagaId,
                    status,
                    state.CurrentState,
                    published,
                    scheduled);
                await ObserveTransitionAsync(observation with { State = result.State, Status = result.Status }, cancellationToken)
                    .ConfigureAwait(false);

                return result;
            }

            throw lastConflict ?? new SagaConcurrencyException(sagaId, machine.StateType, -1, -1);
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch(Exception error) {
            await ObserveFaultAsync(observation, error, CancellationToken.None).ConfigureAwait(false);

            throw;
        }
    }

    private Task ObserveEventAsync(SagaObservationContext context, CancellationToken cancellationToken) =>
        _observers is null ? Task.CompletedTask : _observers.OnEventAsync(context, cancellationToken);

    private Task ObserveTransitionAsync(SagaObservationContext context, CancellationToken cancellationToken) =>
        _observers is null ? Task.CompletedTask : _observers.OnTransitionAsync(context, cancellationToken);

    private Task ObserveFaultAsync(SagaObservationContext context, Exception error, CancellationToken cancellationToken) =>
        _observers is null ? Task.CompletedTask : _observers.OnFaultAsync(context, error, cancellationToken);

    private static async Task<(SagaProgress Progress, long Version)> RunCompensationsAsync( // NOSONAR: Public options/records intentionally group related settings; splitting would fragment the API.
        ISagaRuntime machine,
        ISagaStore store,
        IServiceProvider scopeProvider,
        string sagaId,
        ISagaState state,
        SagaProgress progress,
        long version,
        CancellationToken cancellationToken) {
        var compensated = new HashSet<string>(progress.CompensatedSteps, StringComparer.Ordinal);
        ICqrsBroker? broker = null;

        for(var index = progress.CompletedSteps.Count - 1; index >= 0; index--) {
            var stepId = progress.CompletedSteps[index];

            if(compensated.Contains(stepId)) {
                continue;
            }

            if(!machine.TryGetCompensation(stepId, out var compensation)) {
                throw new InvalidOperationException(
                    $"The saga machine '{machine.StateType.Name}' has no compensation for completed step '{stepId}'.");
            }

            broker ??= scopeProvider.GetRequiredService<ICqrsBroker>();
            var step = compensation.CreateStep(state);
            await step.Dispatch(broker, cancellationToken).ConfigureAwait(false);

            compensated.Add(stepId);
            progress = new SagaProgress(progress.CompletedSteps, compensated);
            await store.SaveAsync(sagaId, machine.StateType, state, version, progress, cancellationToken).ConfigureAwait(false);
            version++;
        }

        return (progress, version);
    }

    private static SagaProgress AddCompletedSteps(SagaProgress progress, SagaEventDefinition definition) {
        if(definition.Compensations.Count is 0) {
            return progress;
        }

        var completed = new List<string>(progress.CompletedSteps);

        foreach(var compensation in definition.Compensations) { // NOSONAR: Explicit loop is clearer and faster than a LINQ pipeline here.
            if(!completed.Contains(compensation.StepId, StringComparer.Ordinal)) {
                completed.Add(compensation.StepId);
            }
        }

        return new SagaProgress(completed, progress.CompensatedSteps);
    }
}

/// <summary>Validates saga configuration at host startup before the first event.</summary>
internal sealed class SagaDispatcherHostedService(SagaDispatcher dispatcher) : IHostedService {
    private readonly SagaDispatcher _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) {
        _dispatcher.EnsureConfigured();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}