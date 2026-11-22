//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.RequestResponse;

/// <summary>How a request client collects replies.</summary>
public enum RequestResponseMode {
    /// <summary>Complete with the first response; later responses are ignored.</summary>
    First = 0,

    /// <summary>Collect every response until the timeout expires.</summary>
    All = 1,
}

/// <summary>Well-known headers used by request/response envelopes.</summary>
public static class RequestResponseHeaders {
    /// <summary>Header carrying <c>request</c> or <c>response</c>.</summary>
    public const string Kind = "x-cqrs-request-kind";

    /// <summary>Header carrying the request id (also copied to the envelope correlation id).</summary>
    public const string RequestId = "x-cqrs-request-id";

    /// <summary>Value for an outbound request.</summary>
    public const string Request = "request";

    /// <summary>Value for a response.</summary>
    public const string Response = "response";
}