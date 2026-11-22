//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Http;

/// <summary>Thrown when the remote HTTP endpoint rejects a message.</summary>
public sealed class HttpTransportException : InvalidOperationException {
    /// <summary>Initializes the exception.</summary>
    /// <param name="statusCode">The HTTP response status.</param>
    /// <param name="responseBody">The response body, when available.</param>
    /// <param name="message">The human-readable failure.</param>
    public HttpTransportException(System.Net.HttpStatusCode statusCode, string? responseBody, string message) : base(message) {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    /// <summary>Gets the HTTP response status.</summary>
    public System.Net.HttpStatusCode StatusCode { get; }

    /// <summary>Gets the response body, when available.</summary>
    public string? ResponseBody { get; }
}
