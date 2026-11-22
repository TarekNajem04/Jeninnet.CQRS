//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Validates receive endpoints once when the host starts. A bad endpoint aborts startup with the
/// endpoint name in the exception instead of failing on the first delivered message.
/// </summary>
/// <param name="endpoints">The configured endpoint snapshot.</param>
public sealed class EndpointConfigurationHostedService(EndpointConfigurationRegistry endpoints) : IHostedService {
    private readonly EndpointConfigurationRegistry _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) {
        _endpoints.ValidateAll();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}