//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Reports an invalid receive-endpoint configuration. The endpoint name is carried on the
/// exception so startup logs identify the misconfigured endpoint without parsing the message.
/// </summary>
public sealed class EndpointConfigurationException : InvalidOperationException {
    /// <summary>Initializes the exception for <paramref name="endpointName"/>.</summary>
    /// <param name="endpointName">The endpoint being validated, or the name that could not be resolved.</param>
    /// <param name="message">The human-readable validation failure.</param>
    public EndpointConfigurationException(string endpointName, string message) : base(message) {
        EndpointName = endpointName ?? string.Empty;
    }

    /// <summary>Gets the endpoint name that failed validation.</summary>
    public string EndpointName { get; }
}
