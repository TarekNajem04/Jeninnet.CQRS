//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Diagnostics;

/// <summary>
/// Well-known diagnostic names. Declared once so behaviors, exporters, and tests cannot drift apart.
/// </summary>
public static class CqrsDiagnosticNames {
    /// <summary>Gets the <see cref="ActivitySource"/> name.</summary>
    public const string SOURCE_NAME = "Jeninnet.CQRS";

    /// <summary>Gets the <see cref="Meter"/> name.</summary>
    public const string METER_NAME = "Jeninnet.CQRS";

    /// <summary>Gets the span name started per dispatch.</summary>
    public const string DISPATCH_ACTIVITY = "Dispatch";

    /// <summary>Gets the dispatch-duration histogram name.</summary>
    public const string DISPATCH_DURATION = "dispatch.duration";

    /// <summary>Gets the dispatch-error counter name.</summary>
    public const string DISPATCH_ERRORS = "dispatch.errors";

    /// <summary>Gets the milliseconds unit used by <see cref="DISPATCH_DURATION"/>.</summary>
    public const string MILLISECONDS_UNIT = "ms";

    /// <summary>Gets the request-type tag key.</summary>
    public const string REQUEST_TYPE_TAG = "cqrs.request";

    /// <summary>Gets the response-type tag key.</summary>
    public const string RESPONSE_TYPE_TAG = "cqrs.response";

    /// <summary>Gets the outcome tag key (<c>success</c>, <c>failure</c>, or <c>cancelled</c>).</summary>
    public const string OUTCOME_TAG = "cqrs.outcome";

    /// <summary>Gets the exception-type tag key (errors only).</summary>
    public const string EXCEPTION_TYPE_TAG = "cqrs.exception";

    /// <summary>Gets the deferred-backlog observable gauge name.</summary>
    public const string DEFERRED_PENDING = "cqrs.deferred.pending";

    /// <summary>Gets the oldest deferred due-time observable gauge name (seconds).</summary>
    public const string DEFERRED_OLDEST_DUE_SECONDS = "cqrs.deferred.oldest_due_seconds";

    /// <summary>Gets the outbox-backlog observable gauge name.</summary>
    public const string OUTBOX_PENDING = "cqrs.outbox.pending";

    /// <summary>Gets the transport outbox published counter name.</summary>
    public const string OUTBOX_PUBLISHED = OutboxRelayDiagnosticNames.Published;

    /// <summary>Gets the transport outbox failed counter name.</summary>
    public const string OUTBOX_FAILED = OutboxRelayDiagnosticNames.Failed;

    /// <summary>Gets the transport outbox lag gauge name (seconds).</summary>
    public const string OUTBOX_LAG_SECONDS = OutboxRelayDiagnosticNames.LagSeconds;

    /// <summary>Gets the dead-letter-count observable gauge name.</summary>
    public const string DEADLETTER_COUNT = "cqrs.deadletter.count";

    /// <summary>Gets the store-type tag key used by durability gauges.</summary>
    public const string STORE_TAG = "cqrs.store";

    /// <summary>Gets the seconds unit used by the oldest-due gauge.</summary>
    public const string SECONDS_UNIT = "s";
}

/// <summary>
/// Shared <see cref="ActivitySource"/> and <see cref="Meter"/> for CQRS dispatch diagnostics.
/// The behavior does all listening checks; this type only owns the instruments.
/// </summary>
public static class CqrsDiagnostics {
    /// <summary>Gets the activity source sampled by tracers.</summary>
    public static readonly ActivitySource ActivitySource = new(CqrsDiagnosticNames.SOURCE_NAME, "1.0.0");

    /// <summary>Gets the meter read by metrics exporters.</summary>
    public static readonly Meter Meter = new(CqrsDiagnosticNames.METER_NAME, "1.0.0");

    /// <summary>Gets the per-dispatch duration histogram, in milliseconds.</summary>
    public static readonly Histogram<double> DispatchDuration = Meter.CreateHistogram<double>(
        CqrsDiagnosticNames.DISPATCH_DURATION,
        CqrsDiagnosticNames.MILLISECONDS_UNIT,
        "Elapsed wall-clock time per dispatched request.");

    /// <summary>Gets the dispatch-failure counter.</summary>
    public static readonly Counter<long> DispatchErrors = Meter.CreateCounter<long>(
        CqrsDiagnosticNames.DISPATCH_ERRORS,
        description: "Requests whose handler threw.");
}
