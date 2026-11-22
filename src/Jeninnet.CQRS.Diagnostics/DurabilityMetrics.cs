//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Diagnostics;

/// <summary>
/// Which durability stores were registered when observability was enabled. Absent stores get no
/// instruments, so exporters never see empty or fabricated series.
/// </summary>
internal readonly record struct DurabilitySignals(bool Deferred, bool Outbox, bool DeadLetters);

/// <summary>
/// Owns the durability observable gauges. Gauges read the registered stores at collection time
/// through the existing <c>ListAsync</c> methods; no new storage and no background loop.
/// </summary>
/// <remarks>
/// Observable-gauge callbacks are synchronous by contract, so a store read is awaited inline.
/// Scrapes are infrequent; in-memory stores complete synchronously. A failing read emits no
/// measurement rather than a misleading zero; the durability health checks surface the failure.
/// The meter is per registration and disposed with the provider.
/// </remarks>
internal sealed class DurabilityMetrics : IDisposable {
    private readonly IServiceProvider _provider;
    private readonly TimeProvider _clock;
    private readonly Meter _meter;

    internal DurabilityMetrics(IServiceProvider provider, TimeProvider clock, DurabilitySignals signals) {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _meter = new Meter(CqrsDiagnosticNames.METER_NAME, "1.0.0");

        if(signals.Deferred) {
            _meter.CreateObservableGauge(
                CqrsDiagnosticNames.DEFERRED_PENDING,
                ObserveDeferredPending,
                unit: "{messages}",
                description: "Deferred commands staged and not yet completed.");
            _meter.CreateObservableGauge(
                CqrsDiagnosticNames.DEFERRED_OLDEST_DUE_SECONDS,
                ObserveDeferredOldestDueSeconds,
                CqrsDiagnosticNames.SECONDS_UNIT,
                "Age of the oldest deferred command past its due time (zero when on time or empty).");
        }

        if(signals.Outbox) {
            _meter.CreateObservableGauge(
                CqrsDiagnosticNames.OUTBOX_PENDING,
                ObserveOutboxPending,
                unit: "{messages}",
                description: "Notifications staged in the outbox and not yet completed.");
        }

        if(signals.DeadLetters) {
            _meter.CreateObservableGauge(
                CqrsDiagnosticNames.DEADLETTER_COUNT,
                ObserveDeadLetterCount,
                unit: "{messages}",
                description: "Messages currently in the dead-letter store.");
        }
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();

    private IEnumerable<Measurement<long>> ObserveDeferredPending() {
        var store = _provider.GetService<IDeferredStore>();

        if(store is null) {
            return [];
        }

        try {
            var pending = store.ListAsync().GetAwaiter().GetResult();

            return [new Measurement<long>(pending.Count, StoreTag(store))];
        }
        catch(Exception) {
            return [];
        }
    }

    private IEnumerable<Measurement<double>> ObserveDeferredOldestDueSeconds() {
        var store = _provider.GetService<IDeferredStore>();

        if(store is null) {
            return [];
        }

        try {
            var pending = store.ListAsync().GetAwaiter().GetResult();
            var oldest = pending.Count is 0
                ? 0d
                : Math.Max(0d, (_clock.GetUtcNow() - pending.Min(static item => item.Envelope.DueUtc)).TotalSeconds);

            return [new Measurement<double>(oldest, StoreTag(store))];
        }
        catch(Exception) {
            return [];
        }
    }

    private IEnumerable<Measurement<long>> ObserveOutboxPending() {
        var store = _provider.GetService<IOutboxStore>();

        if(store is null) {
            return [];
        }

        try {
            var pending = store.ListAsync().GetAwaiter().GetResult();

            return [new Measurement<long>(pending.Count, StoreTag(store))];
        }
        catch(Exception) {
            return [];
        }
    }

    private IEnumerable<Measurement<long>> ObserveDeadLetterCount() {
        var store = _provider.GetService<IDeadLetterStore>();

        if(store is null) {
            return [];
        }

        try {
            var entries = store.ListAsync().GetAwaiter().GetResult();

            return [new Measurement<long>(entries.Count, StoreTag(store))];
        }
        catch(Exception) {
            return [];
        }
    }

    private static KeyValuePair<string, object?> StoreTag(object store) =>
        new(CqrsDiagnosticNames.STORE_TAG, store.GetType().Name);
}

/// <summary>
/// Creates the durability metrics at host startup so the observable gauges exist even before the
/// first scrape. It has no work to do; the gauges read stores when collected.
/// </summary>
/// <param name="metrics">The metrics owner. Never <see langword="null"/>.</param>
internal sealed class DurabilityMetricsHostedService(DurabilityMetrics metrics) : IHostedService {
    private readonly DurabilityMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) {
        GC.KeepAlive(_metrics);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
