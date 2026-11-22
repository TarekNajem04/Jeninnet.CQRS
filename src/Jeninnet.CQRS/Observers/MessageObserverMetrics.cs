//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Observers;

/// <summary>Metric names and tags for message-observer failures.</summary>
public static class MessageObserverDiagnosticNames {
    /// <summary>The meter shared with the rest of the CQRS diagnostics.</summary>
    public const string MeterName = "Jeninnet.CQRS";

    /// <summary>Counter for observer exceptions isolated by the invoker.</summary>
    public const string Faults = "cqrs.observer.faults";

    /// <summary>Tag carrying the observer CLR type name.</summary>
    public const string ObserverTag = "cqrs.observer.type";

    /// <summary>Tag carrying the lifecycle operation.</summary>
    public const string OperationTag = "cqrs.observer.operation";
}

/// <summary>
/// Counts observer faults. An observer that throws is isolated by the invoker; this counter makes
/// the failure visible without breaking messaging.
/// </summary>
public sealed class MessageObserverMetrics : IDisposable {
    private readonly Meter _meter;
    private readonly Counter<long> _faults;

    /// <summary>Initializes the instruments.</summary>
    public MessageObserverMetrics() {
        _meter = new Meter(MessageObserverDiagnosticNames.MeterName, "1.0.0");
        _faults = _meter.CreateCounter<long>(
            MessageObserverDiagnosticNames.Faults,
            unit: "{faults}",
            description: "Message observer exceptions isolated by the invoker.");
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();

    /// <summary>Records one isolated observer failure.</summary>
    /// <param name="observerType">The observer CLR type name.</param>
    /// <param name="operation">The lifecycle operation.</param>
    public void RecordFault(string observerType, string operation) =>
        _faults.Add(1,
            new KeyValuePair<string, object?>(MessageObserverDiagnosticNames.ObserverTag, observerType),
            new KeyValuePair<string, object?>(MessageObserverDiagnosticNames.OperationTag, operation));
}