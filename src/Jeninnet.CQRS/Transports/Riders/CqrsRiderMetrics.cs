//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>Well-known rider metric names and tag keys.</summary>
public static class CqrsRiderDiagnosticNames {
    /// <summary>Gets the meter name owned by the rider host.</summary>
    public const string MeterName = "Jeninnet.CQRS.Riders";

    /// <summary>Gets the counter incremented when a rider starts.</summary>
    public const string Started = "cqrs.rider.started";

    /// <summary>Gets the counter incremented when a rider stops.</summary>
    public const string Stopped = "cqrs.rider.stopped";

    /// <summary>Gets the counter incremented when a rider start or stop fails.</summary>
    public const string Faults = "cqrs.rider.faults";

    /// <summary>Gets the tag key carrying the rider name.</summary>
    public const string RiderTag = "cqrs.rider";

    /// <summary>Gets the tag key carrying the transport type name.</summary>
    public const string TransportTag = "cqrs.rider.transport";
}

/// <summary>
/// Per-rider lifecycle instruments. Every measurement carries the rider name and the concrete
/// transport type, so one dashboard can split by rider without parsing metric names.
/// </summary>
public sealed class CqrsRiderMetrics : IDisposable {
    private readonly Meter _meter;
    private readonly Counter<long> _started;
    private readonly Counter<long> _stopped;
    private readonly Counter<long> _faults;

    /// <summary>Initializes the rider instruments.</summary>
    public CqrsRiderMetrics() {
        _meter = new Meter(CqrsRiderDiagnosticNames.MeterName, "1.0.0");
        _started = _meter.CreateCounter<long>(CqrsRiderDiagnosticNames.Started, unit: "{riders}", description: "Riders that started successfully.");
        _stopped = _meter.CreateCounter<long>(CqrsRiderDiagnosticNames.Stopped, unit: "{riders}", description: "Riders that stopped cleanly.");
        _faults = _meter.CreateCounter<long>(CqrsRiderDiagnosticNames.Faults, unit: "{events}", description: "Rider start or stop failures.");
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();

    internal void RecordStarted(CqrsRider rider) => _started.Add(1, Tags(rider));

    internal void RecordStopped(CqrsRider rider) => _stopped.Add(1, Tags(rider));

    internal void RecordFault(CqrsRider rider) => _faults.Add(1, Tags(rider));

    private static KeyValuePair<string, object?>[] Tags(CqrsRider rider) =>
        [
            new(CqrsRiderDiagnosticNames.RiderTag, rider.Name),
            new(CqrsRiderDiagnosticNames.TransportTag, rider.Transport.GetType().Name),
        ];
}
