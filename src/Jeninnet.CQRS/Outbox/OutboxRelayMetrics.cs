//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Outbox;

/// <summary>
/// Owns the relay counters and the outbox lag gauge. The gauge reads the store when collected,
/// matching the durability gauges; a failed read emits no measurement rather than a fake zero.
/// </summary>
public sealed class OutboxRelayMetrics : IDisposable {
    private readonly IOutboxStore _store;
    private readonly TimeProvider _clock;
    private readonly Meter _meter;
    private readonly Counter<long> _published;
    private readonly Counter<long> _failed;

    /// <summary>Initializes the instruments.</summary>
    /// <param name="store">The outbox store used by the lag gauge. Never <see langword="null"/>.</param>
    /// <param name="clock">The clock used for lag. Never <see langword="null"/>.</param>
    public OutboxRelayMetrics(IOutboxStore store, TimeProvider clock) {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _meter = new Meter(OutboxRelayDiagnosticNames.MeterName, "1.0.0");
        _published = _meter.CreateCounter<long>(
            OutboxRelayDiagnosticNames.Published,
            unit: "{messages}",
            description: "Outbox rows accepted by the transport.");
        _failed = _meter.CreateCounter<long>(
            OutboxRelayDiagnosticNames.Failed,
            unit: "{messages}",
            description: "Outbox publish attempts that failed.");
        _meter.CreateObservableGauge(
            OutboxRelayDiagnosticNames.LagSeconds,
            ObserveLag,
            OutboxRelayDiagnosticNames.SecondsUnit,
            "Age of the oldest pending outbox row (zero when empty).");
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();

    /// <summary>Records one successful transport publish.</summary>
    /// <param name="transport">The registered transport name.</param>
    /// <param name="messageType">The published message type.</param>
    public void RecordPublished(string transport, Type messageType) {
        Guard.NotNullOrWhiteSpace(transport);
        Guard.NotNull(messageType);

        _published.Add(1,
            new KeyValuePair<string, object?>(OutboxRelayDiagnosticNames.TransportTag, transport),
            new KeyValuePair<string, object?>(OutboxRelayDiagnosticNames.MessageTag, messageType.Name));
    }

    /// <summary>Records one failed transport publish.</summary>
    /// <param name="transport">The registered transport name.</param>
    /// <param name="messageType">The message type that failed.</param>
    /// <param name="deadLettered">Whether the failure reached the poison threshold.</param>
    public void RecordFailed(string transport, Type messageType, bool deadLettered) {
        Guard.NotNullOrWhiteSpace(transport);
        Guard.NotNull(messageType);

        _failed.Add(1,
            new KeyValuePair<string, object?>(OutboxRelayDiagnosticNames.TransportTag, transport),
            new KeyValuePair<string, object?>(OutboxRelayDiagnosticNames.MessageTag, messageType.Name),
            new KeyValuePair<string, object?>(OutboxRelayDiagnosticNames.OutcomeTag, deadLettered ? "dead-letter" : "retry"));
    }

    private IEnumerable<Measurement<double>> ObserveLag() {
        try {
            var pending = _store.ListAsync().GetAwaiter().GetResult();

            if(pending.Count is 0) {
                return [new Measurement<double>(0, StoreTag())];
            }

            var oldest = pending.Min(static item => item.Notification.DueUtc);
            var lag = _clock.GetUtcNow() - oldest;

            return [new Measurement<double>(lag < TimeSpan.Zero ? 0 : lag.TotalSeconds, StoreTag())];
        }
        catch(Exception) {
            return [];
        }
    }

    private KeyValuePair<string, object?> StoreTag() =>
        new(OutboxRelayDiagnosticNames.StoreTag, _store.GetType().Name);
}
