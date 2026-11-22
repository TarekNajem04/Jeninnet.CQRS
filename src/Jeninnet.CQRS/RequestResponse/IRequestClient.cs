//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.RequestResponse;

/// <summary>
/// Request/response client over <see cref="IMessageTransport"/>. Requests and responses are
/// published envelopes: the request carries <see cref="RequestResponseHeaders.RequestId"/> and
/// the response copies it into the correlation id. A first-response client completes on the first
/// matching reply; the all-responses overload waits out the timeout and aggregates every reply.
/// </summary>
/// <typeparam name="TRequest">The request message type.</typeparam>
/// <typeparam name="TResponse">The response message type.</typeparam>
public interface IRequestClient<TRequest, TResponse> {
    /// <summary>Sends a request and waits for the first response.</summary>
    /// <param name="request">The request instance.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="RequestTimeoutException">No response arrived before the timeout.</exception>
    Task<TResponse> GetResponseAsync(TRequest request, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Sends a request using <see cref="RequestResponseOptions.DefaultTimeout"/>.</summary>
    /// <param name="request">The request instance.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<TResponse> GetResponseAsync(TRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sends a request and collects every response until the timeout expires.</summary>
    /// <param name="request">The request instance.</param>
    /// <param name="timeout">How long to collect responses.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="RequestTimeoutException">No response arrived before the timeout.</exception>
    Task<IReadOnlyList<TResponse>> GetAllResponsesAsync(TRequest request, TimeSpan timeout, CancellationToken cancellationToken = default);
}