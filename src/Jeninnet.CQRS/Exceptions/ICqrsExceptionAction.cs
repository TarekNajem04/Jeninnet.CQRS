//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Exceptions;

/// <summary>
/// Non-generic view of a typed exception action: observes a failure without handling it.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
public interface ICqrsExceptionAction<in TRequest> {
    /// <summary>Gets the exact exception type this action applies to.</summary>
    Type ExceptionType { get; }

    /// <summary>Observes the failure. The exception still propagates.</summary>
    Task HandleAsync(TRequest request, Exception exception, CancellationToken cancellationToken);
}

/// <summary>
/// Base class for an action that observes one exception type. Actions never produce a response.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TException">The exception type observed.</typeparam>
public abstract class ExceptionAction<TRequest, TException> : ICqrsExceptionAction<TRequest>
    where TException : Exception {
    /// <inheritdoc />
    public Type ExceptionType => typeof(TException);

    /// <inheritdoc />
    public Task HandleAsync(TRequest request, Exception exception, CancellationToken cancellationToken) =>
        HandleCoreAsync(request, (TException)exception, cancellationToken);

    /// <summary>
    /// Observes the typed exception.
    /// </summary>
    /// <param name="request">The request that failed.</param>
    /// <param name="exception">The typed exception.</param>
    /// <param name="cancellationToken">The dispatch token.</param>
    protected abstract Task HandleCoreAsync(TRequest request, TException exception, CancellationToken cancellationToken);
}
