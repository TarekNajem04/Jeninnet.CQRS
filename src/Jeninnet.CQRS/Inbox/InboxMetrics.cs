//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Inbox;

/// <summary>
/// Owns the inbox counters. Duplicate deliveries increment
/// <see cref="InboxDiagnosticNames.Duplicates"/>; successful handler completions and pruned rows
/// have their own counters so operators can distinguish replay protection from cleanup activity.
/// </summary>
public sealed class InboxMetrics : IDisposable {
    private readonly Meter _meter;
    private readonly Counter<long> _duplicates;
    private readonly Counter<long> _processed;
    private readonly Counter<long> _pruned;

    /// <summary>Initializes the instruments.</summary>
    public InboxMetrics() {
        _meter = new Meter(InboxDiagnosticNames.MeterName, "1.0.0");
        _duplicates = _meter.CreateCounter<long>(
            InboxDiagnosticNames.Duplicates,
            unit: "{messages}",
            description: "Inbound deliveries dropped because their message id was already seen.");
        _processed = _meter.CreateCounter<long>(
            InboxDiagnosticNames.Processed,
            unit: "{messages}",
            description: "Inbound deliveries whose handler completed and were recorded.");
        _pruned = _meter.CreateCounter<long>(
            InboxDiagnosticNames.Pruned,
            unit: "{rows}",
            description: "Inbox rows removed by the background pruner.");
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();

    /// <summary>Records one delivery dropped as a duplicate.</summary>
    /// <param name="messageType">The envelope's message type name.</param>
    public void RecordDuplicate(string messageType) =>
        _duplicates.Add(1, Tag(messageType));

    /// <summary>Records one handler completion recorded in the inbox.</summary>
    /// <param name="messageType">The envelope's message type name.</param>
    public void RecordProcessed(string messageType) =>
        _processed.Add(1, Tag(messageType));

    /// <summary>Records rows removed by a prune pass.</summary>
    /// <param name="count">The number of rows removed.</param>
    public void RecordPruned(int count) {
        if(count > 0) {
            _pruned.Add(count);
        }
    }

    private static KeyValuePair<string, object?> Tag(string messageType) =>
        new(InboxDiagnosticNames.MessageTypeTag, messageType);
}
