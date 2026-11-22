//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Interfaces;

/// <summary>
/// Defines a contract for handling requests of type <typeparamref name="TRequest"/> and producing responses of type <typeparamref name="TResponse"/>.
/// </summary>
/// <remarks>
/// This interface is typically implemented by classes that encapsulate specific request-handling logic.
/// It supports asynchronous operations and allows for cancellation via a <see cref="CancellationToken"/>.
/// </remarks>
/// <typeparam name="TRequest">The type of the request to be handled.</typeparam>
/// <typeparam name="TResponse">The type of the response to be returned.</typeparam>
public interface IHandler<in TRequest, TResponse> {
    /// <summary>
    /// Handles the specified request asynchronously and returns a response.
    /// </summary>
    /// <param name="request">The request object to process. Cannot be <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the response object.</returns>
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}
