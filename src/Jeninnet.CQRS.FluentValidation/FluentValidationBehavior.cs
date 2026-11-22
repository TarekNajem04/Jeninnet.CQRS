//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.FluentValidation;

/// <summary>
/// Runs all <see cref="IValidator{T}"/> registered for the request type and throws <see cref="ValidationException"/> on failures.
/// </summary>
/// <remarks>
/// Initializes the behavior with all validators for <typeparamref name="TRequest"/>.
/// </remarks>
internal sealed class FluentValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators) : IPipelineBehavior<TRequest, TResponse> {
    private readonly IEnumerable<IValidator<TRequest>> _validators = validators ?? throw new ArgumentNullException(nameof(validators));

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(TRequest request, Func<TRequest, CancellationToken, Task<TResponse>> next, CancellationToken cancellationToken) {
        if(_validators.Any()) {
            var context = new ValidationContext<TRequest>(request);
            var failures = _validators.Select(v => v.Validate(context))
                                      .SelectMany(r => r.Errors)
                                      .Where(f => f is not null)
                                      .ToList()
                                      ;

            if(failures.Count != 0) {
                throw new ValidationException(failures);
            }
        }

        return await next(request, cancellationToken).ConfigureAwait(false);
    }
}
