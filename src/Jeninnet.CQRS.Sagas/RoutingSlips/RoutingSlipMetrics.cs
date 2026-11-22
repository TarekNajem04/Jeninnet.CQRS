//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.RoutingSlips;

/// <summary>Metric names and tags for routing-slip execution.</summary>
public static class RoutingSlipDiagnosticNames {
    /// <summary>The meter shared with the rest of the CQRS diagnostics.</summary>
    public const string MeterName = "Jeninnet.CQRS";

    /// <summary>Observable gauge for running and compensating slips.</summary>
    public const string Active = "cqrs.routing_slip.active";

    /// <summary>Counter for slips where every step completed.</summary>
    public const string Completed = "cqrs.routing_slip.completed";

    /// <summary>Counter for slips where a failure was fully compensated.</summary>
    public const string Compensated = "cqrs.routing_slip.compensated";

    /// <summary>Counter for slips with a failed compensation.</summary>
    public const string Faulted = "cqrs.routing_slip.faulted";

    /// <summary>Counter for executed or compensated steps.</summary>
    public const string Step = "cqrs.routing_slip.step";

    /// <summary>Tag carrying the definition name.</summary>
    public const string DefinitionTag = "cqrs.routing_slip.definition";

    /// <summary>Tag carrying the slip status.</summary>
    public const string StatusTag = "cqrs.routing_slip.status";

    /// <summary>Tag carrying the step name.</summary>
    public const string StepTag = "cqrs.routing_slip.step_name";

    /// <summary>Tag carrying the step operation (<c>execute</c> or <c>compensate</c>).</summary>
    public const string OperationTag = "cqrs.routing_slip.operation";

    /// <summary>Tag carrying the step outcome (<c>completed</c> or <c>faulted</c>).</summary>
    public const string OutcomeTag = "cqrs.routing_slip.outcome";
}

/// <summary>
/// Owns the routing-slip instruments. The active gauge counts persisted running/compensating rows;
/// completed, compensated, faulted, and step counters advance at their respective transitions.
/// </summary>
public sealed class RoutingSlipMetrics : IDisposable {
    private readonly IRoutingSlipStore _store;
    private readonly Meter _meter;
    private readonly Counter<long> _completed;
    private readonly Counter<long> _compensated;
    private readonly Counter<long> _faulted;
    private readonly Counter<long> _steps;
    private const string SlipUnit = "{slips}";

    /// <summary>Initializes the instruments.</summary>
    /// <param name="store">The store used by the active gauge.</param>
    public RoutingSlipMetrics(IRoutingSlipStore store) {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _meter = new Meter(RoutingSlipDiagnosticNames.MeterName, "1.0.0");
        _completed = _meter.CreateCounter<long>(
            RoutingSlipDiagnosticNames.Completed,
            unit: SlipUnit,
            description: "Routing slips where every step completed.");
        _compensated = _meter.CreateCounter<long>(
            RoutingSlipDiagnosticNames.Compensated,
            unit: SlipUnit,
            description: "Routing slips where a step failure was fully compensated.");
        _faulted = _meter.CreateCounter<long>(
            RoutingSlipDiagnosticNames.Faulted,
            unit: SlipUnit,
            description: "Routing slips with a failed compensation.");
        _steps = _meter.CreateCounter<long>(
            RoutingSlipDiagnosticNames.Step,
            unit: "{steps}",
            description: "Routing-slip steps executed or compensated.");
        _meter.CreateObservableGauge(
            RoutingSlipDiagnosticNames.Active,
            ObserveActive,
            unit: SlipUnit,
            description: "Routing slips currently running or compensating.");
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();

    /// <summary>Records a completed slip.</summary>
    /// <param name="definitionName">The definition name.</param>
    public void RecordCompleted(string definitionName) =>
        _completed.Add(1, Definition(definitionName));

    /// <summary>Records a fully compensated slip.</summary>
    /// <param name="definitionName">The definition name.</param>
    public void RecordCompensated(string definitionName) =>
        _compensated.Add(1, Definition(definitionName));

    /// <summary>Records a slip whose compensation failed.</summary>
    /// <param name="definitionName">The definition name.</param>
    public void RecordFaulted(string definitionName) =>
        _faulted.Add(1, Definition(definitionName));

    /// <summary>Records one step outcome.</summary>
    /// <param name="definitionName">The definition name.</param>
    /// <param name="stepName">The step name.</param>
    /// <param name="compensation">Whether this was a compensation.</param>
    /// <param name="completed">Whether the operation completed; <see langword="false"/> means faulted.</param>
    public void RecordStep(string definitionName, string stepName, bool compensation, bool completed) {
        _steps.Add(1,
            Definition(definitionName),
            new KeyValuePair<string, object?>(RoutingSlipDiagnosticNames.StepTag, stepName),
            new KeyValuePair<string, object?>(RoutingSlipDiagnosticNames.OperationTag, compensation ? "compensate" : "execute"),
            new KeyValuePair<string, object?>(RoutingSlipDiagnosticNames.OutcomeTag, completed ? "completed" : "faulted"));
    }

    private static KeyValuePair<string, object?> Definition(string definitionName) =>
        new(RoutingSlipDiagnosticNames.DefinitionTag, definitionName);

    private IEnumerable<Measurement<long>> ObserveActive() {
        foreach(var status in new[] { RoutingSlipStatus.Running, RoutingSlipStatus.Compensating }) {
            long count;

            try {
                count = _store.CountAsync(status).GetAwaiter().GetResult();
            }
            catch(Exception) {
                continue;
            }

            yield return new Measurement<long>(
                count,
                new KeyValuePair<string, object?>(RoutingSlipDiagnosticNames.StatusTag, status.ToString()));
        }
    }
}

/// <summary>Creates the routing-slip instruments at host startup so the gauges exist before first scrape.</summary>
internal sealed class RoutingSlipMetricsHostedService(RoutingSlipMetrics metrics) : IHostedService {
    private readonly RoutingSlipMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) {
        GC.KeepAlive(_metrics);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
