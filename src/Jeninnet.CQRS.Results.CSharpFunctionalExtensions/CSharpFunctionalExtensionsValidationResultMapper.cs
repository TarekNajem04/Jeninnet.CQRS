//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Results.CSharpFunctionalExtensions;

/// <summary>
/// Maps a failed validation result to a failed <see cref="Result{TValue}"/>.
/// </summary>
/// <remarks>
/// <see cref="Result{TValue}"/> stores only a string error, so every validation error is
/// preserved textually through <see cref="ValidationErrorFormatter.JoinMessages"/>: member
/// name, severity, message, and code survive; custom state and message parameters do not. Use
/// the ErrorOr or FluentResults adapter when structured per-error metadata is required.
/// </remarks>
/// <typeparam name="TValue">The successful response value type.</typeparam>
public sealed class CSharpFunctionalExtensionsValidationResultMapper<TValue> : IValidationResultMapper<Result<TValue>> {
    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="result"/> is valid.</exception>
    public Result<TValue> Map(ValidationResult result) {
        if(result.IsValid) {
            throw new ArgumentException("The result mapper requires a failed validation result.", nameof(result));
        }

        return Result.Failure<TValue>(ValidationErrorFormatter.JoinMessages(result));
    }
}