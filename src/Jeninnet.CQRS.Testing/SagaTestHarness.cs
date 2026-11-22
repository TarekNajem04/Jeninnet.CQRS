//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Testing;

/// <summary>
/// Unit-test harness for one saga machine. It builds a minimal provider around the real
/// <see cref="SagaDispatcher"/>, records every follow-up command/notification, and loads the typed
/// state after each event. Pass an EF-backed <see cref="ISagaStore"/> to exercise durable
/// persistence; the default is the in-memory store.
/// </summary>
/// <typeparam name="TState">The concrete saga state type.</typeparam>
/// <typeparam name="TEvent">The primary event message type for the typed <c>When</c>/<c>WhenAsync</c> overload.</typeparam>
public sealed class SagaTestHarness<TState, TEvent> : IDisposable
    where TState : class, ISagaState
    where TEvent : class {
    private readonly ServiceProvider _provider;
    private readonly SagaDispatcher _dispatcher;
    private readonly SagaRepository<TState> _repository;
    private readonly SagaRecordingBroker _recorder;

    /// <summary>Initializes the harness for <paramref name="machine"/>.</summary>
    /// <param name="machine">The saga machine; its state type must be <typeparamref name="TState"/>.</param>
    /// <param name="store">An optional store; defaults to a process-local in-memory store.</param>
    /// <param name="clock">The clock used by the default store and dispatcher; defaults to the system clock.</param>
    public SagaTestHarness(ISaga machine, ISagaStore? store = null, TimeProvider? clock = null) {
        Guard.NotNull(machine);

        if(machine.StateType != typeof(TState)) {
            throw new ArgumentException(
                $"The saga machine persists '{machine.StateType.FullName}', not '{typeof(TState).FullName}'.",
                nameof(machine));
        }

        var serializer = new JsonMessageSerializer(Options.Create(new JsonMessageSerializerOptions()));
        var actualStore = store ?? new InMemorySagaStore(serializer, clock);
        _repository = new SagaRepository<TState>(actualStore);
        _recorder = new SagaRecordingBroker();

        var services = new ServiceCollection();
        services.AddSingleton(actualStore);
        services.AddSingleton(machine);
        services.AddSingleton<ICqrsBroker>(_recorder);
        services.AddSingleton(clock ?? TimeProvider.System);
        services.AddOptions<SagaOptions>();
        services.AddSingleton(_ => new SagaMetrics(actualStore, [machine]));
        services.AddSingleton(new SagaObserverDispatcher());
        services.AddSingleton<SagaDispatcher>();

        _provider = services.BuildServiceProvider();
        _dispatcher = _provider.GetRequiredService<SagaDispatcher>();
    }

    /// <summary>Gets the repository over the harness store.</summary>
    public ISagaRepository<TState> Repository => _repository;

    /// <summary>Dispatches the primary event type.</summary>
    /// <param name="event">The event message.</param>
    /// <param name="cancellationToken">Cancels the dispatch.</param>
    /// <returns>The resulting state and follow-up messages.</returns>
    public Task<SagaTestResult<TState>> WhenAsync(TEvent @event, CancellationToken cancellationToken = default) =>
        WhenAsync((object)@event, typeof(TEvent), cancellationToken);

    /// <summary>
    /// Dispatches the primary event type; test-vocabulary alias for
    /// <see cref="WhenAsync(TEvent, CancellationToken)"/>.
    /// </summary>
    /// <param name="event">The event message.</param>
    /// <param name="cancellationToken">Cancels the dispatch.</param>
    /// <returns>The resulting state and follow-up messages.</returns>
    public Task<SagaTestResult<TState>> When(TEvent @event, CancellationToken cancellationToken = default) =>
        WhenAsync(@event, cancellationToken);

    /// <summary>
    /// Dispatches any event type handled by the machine; test-vocabulary alias for
    /// <see cref="WhenAsync(object, Type, CancellationToken)"/>.
    /// </summary>
    /// <param name="event">The event message.</param>
    /// <param name="eventType">The runtime event type.</param>
    /// <param name="cancellationToken">Cancels the dispatch.</param>
    /// <returns>The resulting state and follow-up messages.</returns>
    /// <exception cref="InvalidOperationException">No configured machine handles the event type.</exception>
    public Task<SagaTestResult<TState>> When(object @event, Type eventType, CancellationToken cancellationToken = default) =>
        WhenAsync(@event, eventType, cancellationToken);

    /// <summary>Dispatches any event type handled by the machine.</summary>
    /// <param name="event">The event message.</param>
    /// <param name="eventType">The runtime event type.</param>
    /// <param name="cancellationToken">Cancels the dispatch.</param>
    /// <returns>The resulting state and follow-up messages.</returns>
    /// <exception cref="InvalidOperationException">No configured machine handles the event type.</exception>
    public async Task<SagaTestResult<TState>> WhenAsync(object @event, Type eventType, CancellationToken cancellationToken = default) {
        Guard.NotNull(@event);
        Guard.NotNull(eventType);

        _recorder.Clear();

        var dispatch = await _dispatcher.DispatchAsync(@event, eventType, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"No configured saga machine handles event '{eventType.FullName}'.");
        var entry = await _repository.LoadAsync(dispatch.SagaId, cancellationToken).ConfigureAwait(false);

        return new SagaTestResult<TState>(
            dispatch.SagaId,
            dispatch.Status,
            dispatch.State,
            entry?.State,
            _recorder.Messages);
    }

    /// <inheritdoc />
    public void Dispose() => _provider.Dispose();
}
