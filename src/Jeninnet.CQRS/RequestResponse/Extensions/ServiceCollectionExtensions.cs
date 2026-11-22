//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Extensions;

/// <summary>Registration for request/response over any <see cref="Transports.IMessageTransport"/>.</summary>
public static partial class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the request/response client, the transport handler that routes requests to
    /// <see cref="RequestResponse.IRequestResponder"/> implementations, a pending-request inbox
    /// (in-memory by default), and the TTL pruning service.
    /// </summary>
    /// <remarks>
    /// Request and response messages are published envelopes so every transport's fan-out
    /// delivers them to the request handler. Register the transport after this call (or at least
    /// before the provider is built) so the handler is part of its handler list. A durable inbox
    /// replaces <see cref="RequestResponse.IRequestInbox"/> from the EF package. Tenant,
    /// correlation, and headers flow through the normal transport envelope.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional configuration of <see cref="RequestResponse.RequestResponseOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsRequestResponse(
        this IServiceCollection services,
        Action<RequestResponse.RequestResponseOptions>? configure = null) {
        Guard.NotNull(services);

        services.AddCqrsMessageSerialization();
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<RequestResponse.RequestResponseOptions>();

        if(configure is not null) {
            services.Configure(configure);
        }

        services.TryAddSingleton<RequestResponse.InMemoryRequestInbox>();
        services.TryAddSingleton<RequestResponse.IRequestInbox>(static provider => provider.GetRequiredService<RequestResponse.InMemoryRequestInbox>());
        services.TryAddSingleton<RequestResponse.RequestWaiterRegistry>();
        services.TryAddSingleton<RequestResponse.RequestResponseTransportHandler>();
        services.TryAddTransient(typeof(RequestResponse.IRequestClient<,>), typeof(RequestResponse.RequestClient<,>));

        if(!services.Any(static descriptor => descriptor.ServiceType == typeof(RequestResponse.RequestResponseHandlerMarker))) {
            services.AddSingleton<IMessageTransportHandler>(static provider => provider.GetRequiredService<RequestResponse.RequestResponseTransportHandler>());
            services.AddSingleton<RequestResponse.RequestResponseHandlerMarker>();
        }

        if(!services.Any(static descriptor => descriptor.ServiceType == typeof(RequestResponse.RequestInboxPruningMarker))) {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RequestResponse.RequestInboxPruningService>());
            services.AddSingleton<RequestResponse.RequestInboxPruningMarker>();
        }

        return services;
    }
}