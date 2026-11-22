//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Diagnostics;

/// <summary>
/// Options controlling the diagnostics pipeline behavior.
/// </summary>
public sealed class CqrsDiagnosticsOptions {
    /// <summary>
    /// The default configuration section name for binding.
    /// </summary>
    public static string SectionName { get; internal set; } = "CqrsDiagnostics";

    /// <summary>
    /// Gets or sets whether tracing spans are produced. Defaults to <see langword="true"/>.
    /// When <see langword="false"/>, no <see cref="System.Diagnostics.Activity"/> is started.
    /// </summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>
    /// Gets or sets whether metrics are recorded. Defaults to <see langword="true"/>.
    /// When <see langword="false"/>, no histogram or counter is updated.
    /// </summary>
    public bool EnableMetrics { get; set; } = true;
}