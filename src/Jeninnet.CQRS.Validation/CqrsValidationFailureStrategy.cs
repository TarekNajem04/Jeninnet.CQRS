//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Validation;

/// <summary>
/// How <see cref="ValidationBehavior{TRequest, TResponse}"/> reports a failed validation.
/// </summary>
public enum CqrsValidationFailureStrategy {
    /// <summary>Throw <see cref="ValidationException"/> (default; exceptions stay for control flow).</summary>
    Throw,

    /// <summary>
    /// Return the response produced by the mapper registered for the response type and
    /// skip the handler plus all inner behaviors.
    /// </summary>
    Result,
}