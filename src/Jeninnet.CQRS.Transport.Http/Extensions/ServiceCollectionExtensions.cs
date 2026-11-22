//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Http.Extensions;

/// <summary>Registration for the outbound HTTP transport.</summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the HTTP transport, selects <see cref="CqrsTransportMode.External"/>, and wires the
    /// hosted service that starts/stops it with the host.
    /// </summary>
    /// <remarks>
    /// The application must configure <see cref="HttpTransportOptions.Endpoint"/> and register a
    /// serializer and naming convention (the call supplies defaults). User registrations made
    /// before this call win (<c>TryAdd</c>), including a custom <see cref="IMessageTransport"/>.
    /// The transport has no consumer endpoint: the remote service owns receive-side dispatch.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional configuration of <see cref="HttpTransportOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsHttpTransport(
        this IServiceCollection services,
        Action<HttpTransportOptions>? configure = null) {
        Guard.NotNull(services);

        services.AddCqrsMessageSerialization();
        services.AddCqrsObservers();
        services.AddOptions<HttpTransportOptions>();
        services.AddOptions<CqrsTransportOptions>();
        services.Configure<CqrsTransportOptions>(static options => options.Mode = CqrsTransportMode.External);
        services.TryAddSingleton(new TransportNamingConvention());

        if(configure is not null) {
            services.Configure(configure);
        }

        services.AddHttpClient();
        services.TryAddSingleton<IMessageTransport, HttpTransport>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, HttpTransportHostedService>());

        return services;
    }
}

/// <summary>Starts and stops the registered transport with the host lifetime.</summary>
internal sealed class HttpTransportHostedService(IMessageTransport transport) : IHostedService {
    private readonly IMessageTransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) =>
        _transport.StartAsync(cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) =>
        _transport.StopAsync(cancellationToken);
}