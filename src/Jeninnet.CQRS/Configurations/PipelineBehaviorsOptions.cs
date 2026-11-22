//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Configurations;

/// <summary>
/// Controls which built-in CQRS pipeline behaviors are enabled.
/// </summary>
public record PipelineBehaviorsOptions(
    bool EnableMetrics = true,
    bool EnableErrorHandling = true,
    bool EnableValidation = true,
    bool EnableTransaction = true,
    bool EnableLogging = true
) {
    /// <summary>
    /// When <see langword="true"/>, registers the <see cref="MetricsBehavior{TRequest,TResponse}"/>.
    /// </summary>
    public bool EnableMetrics { get; init; } = EnableMetrics;

    /// <summary>
    /// When <see langword="true"/>, registers the <see cref="ErrorHandlingBehavior{TRequest,TResponse}"/>.
    /// </summary>
    public bool EnableErrorHandling { get; init; } = EnableErrorHandling;

    /// <summary>
    /// When <see langword="true"/>, registers the validation behavior.
    /// </summary>
    public bool EnableValidation { get; init; } = EnableValidation;

    /// <summary>
    /// When <see langword="true"/>, registers the transaction behavior.
    /// </summary>
    public bool EnableTransaction { get; init; } = EnableTransaction;

    /// <summary>
    /// When <see langword="true"/>, registers the <see cref="LoggingBehavior{TRequest,TResponse}"/>.
    /// </summary>
    public bool EnableLogging { get; init; } = EnableLogging;
}
