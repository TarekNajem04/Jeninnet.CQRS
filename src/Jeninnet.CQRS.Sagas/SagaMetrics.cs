//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas;

/// <summary>Metric and activity names for saga execution.</summary>
public static class SagaDiagnosticNames {
    /// <summary>The meter shared with the rest of the CQRS diagnostics.</summary>
    public const string MeterName = "Jeninnet.CQRS";

    /// <summary>The saga activity source name.</summary>
    public const string ActivitySourceName = "Jeninnet.CQRS.Sagas";

    /// <summary>The activity name emitted per saga event.</summary>
    public const string StepActivity = "SagaStep";

    /// <summary>Observable gauge for persisted saga rows.</summary>
    public const string Active = "cqrs.saga.active";

    /// <summary>Counter for transitions marked <c>Completed()</c>.</summary>
    public const string Completed = "cqrs.saga.completed";

    /// <summary>Counter for transitions marked <c>Failure()</c>.</summary>
    public const string Faulted = "cqrs.saga.faulted";

    /// <summary>Tag carrying the state type name.</summary>
    public const string StateTypeTag = "cqrs.saga.state_type";

    /// <summary>Tag carrying the saga id.</summary>
    public const string SagaIdTag = "cqrs.saga.id";

    /// <summary>Tag carrying the event message type.</summary>
    public const string MessageTag = "cqrs.saga.message";

    /// <summary>Tag carrying the current state.</summary>
    public const string StateTag = "cqrs.saga.state";

    /// <summary>Tag carrying the transition outcome.</summary>
    public const string OutcomeTag = "cqrs.saga.outcome";

    /// <summary>Activity source read by tracers.</summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");
}

/// <summary>
/// Owns the saga instruments. The active gauge counts persisted rows per configured machine at
/// collection time; completed/faulted counters advance once per successfully saved terminal
/// transition. A failed count emits no measurement instead of a misleading zero.
/// </summary>
public sealed class SagaMetrics : IDisposable {
    private readonly ISagaStore _store;
    private readonly ISaga[] _machines;
    private readonly Meter _meter;
    private readonly Counter<long> _completed;
    private readonly Counter<long> _faulted;

    /// <summary>Initializes the instruments.</summary>
    /// <param name="store">The saga store used by the active gauge.</param>
    /// <param name="machines">Configured machines.</param>
    public SagaMetrics(ISagaStore store, IEnumerable<ISaga> machines) {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _machines = (machines ?? throw new ArgumentNullException(nameof(machines)))
            .GroupBy(static machine => machine.GetType())
            .Select(static group => group.First())
            .ToArray();
        _meter = new Meter(SagaDiagnosticNames.MeterName, "1.0.0");
        _completed = _meter.CreateCounter<long>(
            SagaDiagnosticNames.Completed,
            unit: "{sagas}",
            description: "Sagas that reached a transition marked Completed().");
        _faulted = _meter.CreateCounter<long>(
            SagaDiagnosticNames.Faulted,
            unit: "{sagas}",
            description: "Sagas that reached a transition marked Failure().");
        _meter.CreateObservableGauge(
            SagaDiagnosticNames.Active,
            ObserveActive,
            unit: "{sagas}",
            description: "Saga rows currently retained per state type.");
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();

    /// <summary>Records a normal completion.</summary>
    /// <param name="stateType">The state type.</param>
    /// <param name="state">The terminal state name.</param>
    public void RecordCompleted(Type stateType, string state) {
        Guard.NotNull(stateType);
        _completed.Add(1,
            new KeyValuePair<string, object?>(SagaDiagnosticNames.StateTypeTag, stateType.Name),
            new KeyValuePair<string, object?>(SagaDiagnosticNames.StateTag, state));
    }

    /// <summary>Records a faulted completion.</summary>
    /// <param name="stateType">The state type.</param>
    /// <param name="state">The terminal state name.</param>
    public void RecordFaulted(Type stateType, string state) {
        Guard.NotNull(stateType);
        _faulted.Add(1,
            new KeyValuePair<string, object?>(SagaDiagnosticNames.StateTypeTag, stateType.Name),
            new KeyValuePair<string, object?>(SagaDiagnosticNames.StateTag, state));
    }

    private IEnumerable<Measurement<long>> ObserveActive() {
        foreach(var machine in _machines) { // NOSONAR: Explicit loop is clearer and faster than a LINQ pipeline here.
            long count;

            try {
                count = _store.CountAsync(machine.StateType).GetAwaiter().GetResult();
            }
            catch(Exception) {
                continue;
            }

            yield return new Measurement<long>(
                count,
                new KeyValuePair<string, object?>(SagaDiagnosticNames.StateTypeTag, machine.StateType.Name));
        }
    }
}

/// <summary>Creates the saga instruments at host startup so the gauges exist before first scrape.</summary>
internal sealed class SagaMetricsHostedService(SagaMetrics metrics) : IHostedService {
    private readonly SagaMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) {
        GC.KeepAlive(_metrics);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}