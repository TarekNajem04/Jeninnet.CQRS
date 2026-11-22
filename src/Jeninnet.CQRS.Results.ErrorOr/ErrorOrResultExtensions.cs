//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Results.ErrorOr;

/// <summary>
/// ErrorOr registration helpers for the CQRS validation pipeline.
/// </summary>
public static class ErrorOrResultExtensions {
    /// <summary>
    /// Configures the pipeline to return <see cref="ErrorOr{T}"/> validation errors instead of
    /// throwing or calling the handler.
    /// </summary>
    /// <typeparam name="TValue">The successful response value type.</typeparam>
    /// <param name="options">The pipeline options.</param>
    /// <returns>The same options for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    public static CqrsValidationOptions UseErrorOrResultAdapter<TValue>(this CqrsValidationOptions options) {
        ArgumentNullException.ThrowIfNull(options);

        return options.UseResultAdapter<ErrorOr<TValue>>(new ErrorOrValidationResultMapper<TValue>());
    }
}
