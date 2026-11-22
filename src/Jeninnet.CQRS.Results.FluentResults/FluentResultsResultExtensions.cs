//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Results.FluentResults;

/// <summary>
/// FluentResults registration helpers for the CQRS validation pipeline.
/// </summary>
public static class FluentResultsResultExtensions {
    /// <summary>
    /// Configures the pipeline to return a failed <see cref="Result{TValue}"/> instead of
    /// throwing or calling the handler.
    /// </summary>
    /// <typeparam name="TValue">The successful response value type.</typeparam>
    /// <param name="options">The pipeline options.</param>
    /// <returns>The same options for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    public static CqrsValidationOptions UseFluentResultsAdapter<TValue>(this CqrsValidationOptions options) {
        ArgumentNullException.ThrowIfNull(options);

        return options.UseResultAdapter<Result<TValue>>(new FluentResultsValidationResultMapper<TValue>());
    }
}