//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Scheduling;

/// <summary>Metric names for the persistent scheduler.</summary>
public static class SchedulerDiagnosticNames {
    /// <summary>The meter shared with the rest of the CQRS diagnostics.</summary>
    public const string MeterName = "Jeninnet.CQRS";

    /// <summary>Observable gauge for schedule rows due at or before now.</summary>
    public const string Due = "cqrs.scheduler.due";

    /// <summary>Counter for schedule rows successfully published.</summary>
    public const string Fired = "cqrs.scheduler.fired";

    /// <summary>Counter for failed publish attempts.</summary>
    public const string Failed = "cqrs.scheduler.failed";

    /// <summary>Tag carrying the schedule kind.</summary>
    public const string KindTag = "cqrs.scheduler.kind";

    /// <summary>Tag carrying the message type name.</summary>
    public const string MessageTag = "cqrs.scheduler.message";
}

/// <summary>Owns the scheduler instruments; the due gauge reads the store at scrape time.</summary>
public sealed class SchedulerMetrics : IDisposable {
    private readonly ISchedulerStore _store;
    private readonly TimeProvider _clock;
    private readonly Meter _meter;
    private readonly Counter<long> _fired;
    private readonly Counter<long> _failed;

    /// <summary>Initializes the instruments.</summary>
    /// <param name="store">The scheduler store.</param>
    /// <param name="clock">The clock used for the due gauge.</param>
    public SchedulerMetrics(ISchedulerStore store, TimeProvider clock) {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _meter = new Meter(SchedulerDiagnosticNames.MeterName, "1.0.0");
        _fired = _meter.CreateCounter<long>(SchedulerDiagnosticNames.Fired, unit: "{messages}", description: "Scheduled messages published by the worker.");
        _failed = _meter.CreateCounter<long>(SchedulerDiagnosticNames.Failed, unit: "{messages}", description: "Scheduled publish attempts that failed.");
        _meter.CreateObservableGauge(SchedulerDiagnosticNames.Due, ObserveDue, unit: "{messages}", description: "Schedule rows due at or before now.");
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();

    /// <summary>Records one successful publish.</summary>
    /// <param name="kind">The schedule kind.</param>
    /// <param name="messageType">The message type name.</param>
    public void RecordFired(ScheduledMessageKind kind, string messageType) =>
        _fired.Add(1,
            new KeyValuePair<string, object?>(SchedulerDiagnosticNames.KindTag, kind.ToString()),
            new KeyValuePair<string, object?>(SchedulerDiagnosticNames.MessageTag, messageType));

    /// <summary>Records one failed publish.</summary>
    /// <param name="kind">The schedule kind.</param>
    /// <param name="messageType">The message type name.</param>
    public void RecordFailed(ScheduledMessageKind kind, string messageType) =>
        _failed.Add(1,
            new KeyValuePair<string, object?>(SchedulerDiagnosticNames.KindTag, kind.ToString()),
            new KeyValuePair<string, object?>(SchedulerDiagnosticNames.MessageTag, messageType));

    private IEnumerable<Measurement<long>> ObserveDue() {
        try {
            return [new Measurement<long>(_store.CountDueAsync(_clock.GetUtcNow()).GetAwaiter().GetResult())];
        }
        catch(Exception) {
            return [];
        }
    }
}

/// <summary>Creates the scheduler instruments at startup so the due gauge exists before first scrape.</summary>
internal sealed class SchedulerMetricsHostedService(SchedulerMetrics metrics) : IHostedService {
    private readonly SchedulerMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) {
        GC.KeepAlive(_metrics);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
