//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Interfaces;

/// <summary>
/// Defines a behavior in the request dispatcher’s pipeline that can intercept, augment, or wrap the handling of a request.
/// </summary>
/// <typeparam name="TRequest">The type of request being processed.</typeparam>
/// <typeparam name="TResponse">The type of response returned by either the next behavior or the final handler.</typeparam>
public interface IPipelineBehavior<TRequest, TResponse> {
    /// <summary>
    /// Handles a request by optionally executing logic before and/or after
    /// invoking <paramref name="next"/>.
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="next">
    /// A delegate representing the next step in the pipeline.
    /// Calling this delegate invokes either the next behavior or the final handler.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns> A task producing the response from either the next behavior or the final handler. </returns>
    Task<TResponse> HandleAsync(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResponse>> next,
        CancellationToken cancellationToken
    );
}

/// <inheritdoc/>
public interface IOrderedPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> {
    /// <summary>
    /// Gets the position of the item within an ordered collection or sequence.
    /// </summary>
    int Order { get; }
}