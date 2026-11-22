//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.RequestResponse;

/// <summary>
/// Handles a request on the responder side. Register one or more implementations for the same
/// request/response pair; every matching responder runs and publishes its response with the
/// request's correlation id.
/// </summary>
public interface IRequestResponder {
    /// <summary>Gets the request type this responder handles.</summary>
    Type RequestType { get; }

    /// <summary>Gets the response type it produces.</summary>
    Type ResponseType { get; }

    /// <summary>Handles the deserialized request and returns the response.</summary>
    /// <param name="request">The request instance.</param>
    /// <param name="cancellationToken">Cancels handling.</param>
    Task<object?> RespondAsync(object request, CancellationToken cancellationToken);
}

/// <summary>Typed convenience base for <see cref="IRequestResponder"/>.</summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public abstract class RequestResponder<TRequest, TResponse> : IRequestResponder {
    /// <inheritdoc />
    public Type RequestType => typeof(TRequest);

    /// <inheritdoc />
    public Type ResponseType => typeof(TResponse);

    async Task<object?> IRequestResponder.RespondAsync(object request, CancellationToken cancellationToken) =>
        await RespondAsync((TRequest)request, cancellationToken).ConfigureAwait(false);

    /// <summary>Handles the typed request.</summary>
    /// <param name="request">The request instance.</param>
    /// <param name="cancellationToken">Cancels handling.</param>
    protected abstract Task<TResponse> RespondAsync(TRequest request, CancellationToken cancellationToken);
}