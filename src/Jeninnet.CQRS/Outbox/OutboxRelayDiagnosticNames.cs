//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Outbox;

/// <summary>
/// Metric names and tag keys for the transport outbox relay. Declared once so exporters and tests
/// cannot drift from the worker.
/// </summary>
public static class OutboxRelayDiagnosticNames {
    /// <summary>The meter shared with the rest of the CQRS diagnostics.</summary>
    public const string MeterName = "Jeninnet.CQRS";

    /// <summary>Counter incremented after the transport accepts a row.</summary>
    public const string Published = "cqrs.outbox.published";

    /// <summary>Counter incremented for every failed publish attempt.</summary>
    public const string Failed = "cqrs.outbox.failed";

    /// <summary>Gauge for the age of the oldest pending outbox row, in seconds.</summary>
    public const string LagSeconds = "cqrs.outbox.lag_seconds";

    /// <summary>Tag carrying the message type name.</summary>
    public const string MessageTag = "cqrs.message";

    /// <summary>Tag carrying the registered transport type name.</summary>
    public const string TransportTag = "cqrs.transport";

    /// <summary>Tag carrying the failure outcome (<c>retry</c> or <c>dead-letter</c>).</summary>
    public const string OutcomeTag = "cqrs.outcome";

    /// <summary>Tag carrying the outbox store implementation type name.</summary>
    public const string StoreTag = "cqrs.store";

    /// <summary>Seconds unit used by <see cref="LagSeconds"/>.</summary>
    public const string SecondsUnit = "s";
}
