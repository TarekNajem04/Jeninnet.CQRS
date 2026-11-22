//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Validation;

/// <summary>
/// Maps a failed validation result to a CQRS response type, such as
/// <c>ErrorOr&lt;T&gt;</c> or <c>FluentResults.Result&lt;T&gt;</c>.
/// </summary>
/// <typeparam name="TResponse">The CQRS response type.</typeparam>
public interface IValidationResultMapper<out TResponse> {
    /// <summary>
    /// Maps the validation result to a response. The mapper is called only for a failed result.
    /// </summary>
    /// <param name="result">The failed validation result.</param>
    /// <returns>The response returned from the pipeline.</returns>
    /// <exception cref="ArgumentException"><paramref name="result"/> is valid.</exception>
    TResponse Map(ValidationResult result);
}