//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Inbox;

/// <summary>Metric and meter names owned by the consumer inbox.</summary>
public static class InboxDiagnosticNames {
    /// <summary>The meter that owns inbox counters.</summary>
    public const string MeterName = "Jeninnet.CQRS.Inbox";

    /// <summary>Counts deliveries dropped because the message id was already seen.</summary>
    public const string Duplicates = "cqrs.inbox.duplicates";

    /// <summary>Counts deliveries whose handler completed and were recorded.</summary>
    public const string Processed = "cqrs.inbox.processed";

    /// <summary>Counts rows removed by the background pruner.</summary>
    public const string Pruned = "cqrs.inbox.pruned";

    /// <summary>The counter tag carrying the envelope's message type name.</summary>
    public const string MessageTypeTag = "message-type";
}