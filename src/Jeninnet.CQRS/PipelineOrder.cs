//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS;

/// <summary>
/// Provides standard execution order values for pipeline components.
/// </summary>
/// <remarks>
/// These constants define the relative order in which common pipeline behaviors should be executed.
/// Lower values indicate earlier execution in the pipeline.
/// Use these values to assign consistent ordering to middleware or behaviors when constructing a processing pipeline.
/// </remarks>
public static class PipelineOrder {
    /// <summary>OpenTelemetry span/metric behavior; outermost built-in.</summary>
    public const int DIAGNOSTICS = 50;

    /// <summary>Exception-to-response behavior.</summary>
    public const int ERROR_HANDLING = 100;

    /// <summary>Structured logging behavior.</summary>
    public const int LOGGING = 200;

    /// <summary>Metrics behavior.</summary>
    public const int METRICS = 300;

    /// <summary>User behaviors run just outside the transaction.</summary>
    public const int CUSTOM = 999;

    /// <summary>Innermost built-in: wraps the handler only.</summary>
    public const int TRANSACTION = 1000;
}