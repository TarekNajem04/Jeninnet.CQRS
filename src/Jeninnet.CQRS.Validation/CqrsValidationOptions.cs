//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Validation;

/// <summary>
/// Options for the CQRS validation pipeline.
/// </summary>
public sealed class CqrsValidationOptions {
    /// <summary>
    /// Gets or sets the failure strategy. Defaults to <see cref="CqrsValidationFailureStrategy.Throw"/>.
    /// </summary>
    public CqrsValidationFailureStrategy FailureStrategy { get; set; } = CqrsValidationFailureStrategy.Throw;

    private readonly Dictionary<Type, Func<ValidationResult, object?>> _resultMappers = [];

    /// <summary>
    /// Gets the result mapper registered for the specified response type.
    /// </summary>
    /// <param name="responseType">The response type.</param>
    /// <returns>The mapper, or <see langword="null"/> when none is registered.</returns>
    internal Func<ValidationResult, object?>? GetResultMapper(Type responseType) =>
        _resultMappers.TryGetValue(responseType, out var mapper) ? mapper : null;

    /// <summary>
    /// Configures a result adapter and switches the failure strategy to
    /// <see cref="CqrsValidationFailureStrategy.Result"/>. The adapter maps a failed
    /// validation result to the CQRS response type and skips the handler.
    /// </summary>
    /// <typeparam name="TResponse">The CQRS response type.</typeparam>
    /// <param name="mapper">The result mapper.</param>
    /// <returns>The same options for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mapper"/> is null.</exception>
    public CqrsValidationOptions UseResultAdapter<TResponse>(IValidationResultMapper<TResponse> mapper) {
        ArgumentNullException.ThrowIfNull(mapper);

        _resultMappers[typeof(TResponse)] = result => mapper.Map(result);
        FailureStrategy = CqrsValidationFailureStrategy.Result;

        return this;
    }
}