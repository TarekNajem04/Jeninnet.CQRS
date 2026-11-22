//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas;

/// <summary>
/// One invocation helper for saga observers. It caches the observers, exposes a zero-work fast
/// path, and isolates every observer exception so a hook cannot change the saga outcome.
/// </summary>
/// <param name="observers">Registered saga observers in registration order.</param>
/// <param name="metrics">Optional observer-fault counter.</param>
/// <param name="logger">Optional logger for isolated observer exceptions.</param>
public sealed class SagaObserverDispatcher(
    IEnumerable<ISagaObserver>? observers = null,
    MessageObserverMetrics? metrics = null,
    ILogger<SagaObserverDispatcher>? logger = null) {
    private static readonly Action<ILogger, string, string, Exception?> _observerFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(1, "SagaObserverFault"),
            "Saga observer '{Observer}' failed during {Operation}; the saga outcome is unchanged.");

    private readonly ISagaObserver[] _observers = observers?.ToArray() ?? [];
    private readonly MessageObserverMetrics? _metrics = metrics;
    private readonly ILogger<SagaObserverDispatcher>? _logger = logger;

    /// <summary>Gets whether any saga observer is registered.</summary>
    public bool HasObservers => _observers.Length > 0;

    /// <summary>Runs all pre-event observers.</summary>
    /// <param name="context">The saga and event identity.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    public Task OnEventAsync(SagaObservationContext context, CancellationToken cancellationToken = default) =>
        _observers.Length is 0 ? Task.CompletedTask : InvokeAsync(observer => observer.OnSagaEventAsync(context, cancellationToken), "saga-event", cancellationToken);

    /// <summary>Runs all transition observers.</summary>
    /// <param name="context">The final state and status.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    public Task OnTransitionAsync(SagaObservationContext context, CancellationToken cancellationToken = default) =>
        _observers.Length is 0 ? Task.CompletedTask : InvokeAsync(observer => observer.OnSagaTransitionAsync(context, cancellationToken), "saga-transition", cancellationToken);

    /// <summary>Runs all fault observers.</summary>
    /// <param name="context">The saga and event identity.</param>
    /// <param name="error">The failure.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    public Task OnFaultAsync(SagaObservationContext context, Exception error, CancellationToken cancellationToken = default) =>
        _observers.Length is 0 ? Task.CompletedTask : InvokeAsync(observer => observer.OnSagaFaultAsync(context, error, cancellationToken), "saga-fault", cancellationToken);

    private async Task InvokeAsync(Func<ISagaObserver, Task> action, string operation, CancellationToken cancellationToken) {
        foreach(var observer in _observers) {
            try {
                await action(observer).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
                return;
            }
            catch(Exception error) {
                _metrics?.RecordFault(observer.GetType().Name, operation);

                if(_logger is not null) {
                    _observerFailed(_logger, observer.GetType().FullName ?? observer.GetType().Name, operation, error);
                }
            }
        }
    }
}