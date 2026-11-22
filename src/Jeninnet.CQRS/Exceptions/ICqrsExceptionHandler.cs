//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Exceptions;

/// <summary>
/// Non-generic view of a typed exception handler, used by the dispatcher to select handlers by
/// exception type without closing generics at runtime.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface ICqrsExceptionHandler<in TRequest, TResponse> {
    /// <summary>Gets the exact exception type this handler applies to.</summary>
    Type ExceptionType { get; }

    /// <summary>
    /// Handles <paramref name="exception"/> for <paramref name="request"/>.
    /// </summary>
    Task<ExceptionHandlingResult<TResponse>> HandleAsync(TRequest request, Exception exception, CancellationToken cancellationToken);
}

/// <summary>
/// Base class for a handler that turns one exception type into a response.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <typeparam name="TException">The exception type handled.</typeparam>
public abstract class ExceptionHandler<TRequest, TResponse, TException> : ICqrsExceptionHandler<TRequest, TResponse> // NOSONAR: Generic adapter/state-machine arity is part of the public contract.
    where TException : Exception {
    /// <inheritdoc />
    public Type ExceptionType => typeof(TException);

    /// <inheritdoc />
    public Task<ExceptionHandlingResult<TResponse>> HandleAsync(TRequest request, Exception exception, CancellationToken cancellationToken) =>
        HandleCoreAsync(request, (TException)exception, cancellationToken);

    /// <summary>
    /// Handles the typed exception.
    /// </summary>
    /// <param name="request">The request that failed.</param>
    /// <param name="exception">The typed exception.</param>
    /// <param name="cancellationToken">The dispatch token.</param>
    protected abstract Task<ExceptionHandlingResult<TResponse>> HandleCoreAsync(TRequest request, TException exception, CancellationToken cancellationToken);
}
