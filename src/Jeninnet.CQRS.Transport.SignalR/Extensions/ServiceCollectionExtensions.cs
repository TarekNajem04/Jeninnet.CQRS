//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
#if !NETSTANDARD2_0
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Jeninnet.CQRS.Transport.SignalR.Extensions;

#else
namespace Jeninnet.CQRS.Transport.SignalR.Extensions;

#endif

/// <summary>Registration for the SignalR client transport and relay hub.</summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the SignalR client transport, selects <see cref="CqrsTransportMode.External"/>, and
    /// wires the hosted service that starts/stops the connection with the host.
    /// </summary>
    /// <remarks>
    /// The application must call <c>AddSignalR</c> and map the relay hub on the server (use
    /// <c>MapCqrsSignalRHub</c>). User registrations made before this call win (<c>TryAdd</c>).
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional configuration of <see cref="SignalRTransportOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsSignalR(
        this IServiceCollection services,
        Action<SignalRTransportOptions>? configure = null) {
        Guard.NotNull(services);

        services.AddCqrsMessageSerialization();
        services.AddCqrsReceivePipeline();
        services.AddCqrsObservers();
        services.AddOptions<SignalRTransportOptions>();
        services.AddOptions<CqrsTransportOptions>();
        services.Configure<CqrsTransportOptions>(static options => options.Mode = CqrsTransportMode.External);
        services.TryAddSingleton(new TransportNamingConvention());

        if(configure is not null) {
            services.Configure(configure);
        }

        services.TryAddSingleton<IMessageTransport, SignalRTransport>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SignalRTransportHostedService>());

        return services;
    }

#if !NETSTANDARD2_0
    /// <summary>Maps the CQRS relay hub at <paramref name="pattern"/>.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The hub path; defaults to <c>/cqrs</c>.</param>
    /// <returns>The hub endpoint convention builder for further conventions.</returns>
    public static HubEndpointConventionBuilder MapCqrsSignalRHub(this IEndpointRouteBuilder endpoints, string pattern = "/cqrs") {
        Guard.NotNull(endpoints);
        Guard.NotNullOrWhiteSpace(pattern);

        return endpoints.MapHub<CqrsTransportHub>(pattern);
    }
#endif
}

/// <summary>Starts and stops the registered transport with the host lifetime.</summary>
internal sealed class SignalRTransportHostedService(IMessageTransport transport) : IHostedService {
    private readonly IMessageTransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) =>
        _transport.StartAsync(cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) =>
        _transport.StopAsync(cancellationToken);
}
