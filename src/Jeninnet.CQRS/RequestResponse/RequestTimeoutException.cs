//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.RequestResponse;

/// <summary>
/// Thrown when a request receives no response before its timeout. The pending inbox row has
/// already been removed by the client.
/// </summary>
public sealed class RequestTimeoutException : TimeoutException {
    /// <summary>Initializes the exception.</summary>
    /// <param name="requestId">The request correlation id.</param>
    /// <param name="requestType">The request type.</param>
    /// <param name="responseType">The expected response type.</param>
    /// <param name="timeout">The elapsed timeout.</param>
    public RequestTimeoutException(string requestId, Type requestType, Type responseType, TimeSpan timeout)
        : base(BuildMessage(requestId, requestType, responseType, timeout)) {
        RequestId = requestId;
        RequestType = requestType;
        ResponseType = responseType;
        Timeout = timeout;
    }

    private static string BuildMessage(string requestId, Type requestType, Type responseType, TimeSpan timeout) {
        Guard.NotNull(requestId);
        Guard.NotNull(requestType);
        Guard.NotNull(responseType);

        return $"Request '{requestId}' ({requestType.Name}) received no {responseType.Name} response within {timeout}.";
    }

    /// <summary>Gets the request correlation id.</summary>
    public string RequestId { get; }

    /// <summary>Gets the request type.</summary>
    public Type RequestType { get; }

    /// <summary>Gets the expected response type.</summary>
    public Type ResponseType { get; }

    /// <summary>Gets the timeout that elapsed.</summary>
    public TimeSpan Timeout { get; }
}
