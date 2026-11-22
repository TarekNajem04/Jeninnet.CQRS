//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Extensions;

/// <summary>
/// Registration for receive endpoints, receive filters, and the shared consume pipeline.
/// </summary>
public static partial class ServiceCollectionExtensions {
    /// <summary>
    /// Configures the receive endpoint for <typeparamref name="TMessage"/>. The configuration is
    /// validated when the host starts, so an invalid value aborts startup with the endpoint name.
    /// </summary>
    /// <typeparam name="TMessage">The consumed message type.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Configures the endpoint policy.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection ConfigureEndpoint<TMessage>(
        this IServiceCollection services,
        Action<EndpointConfigurationBuilder<TMessage>> configure) {
        Guard.NotNull(services);
        Guard.NotNull(configure);

        var state = GetOrAddEndpointState(services);
        var builder = new EndpointConfigurationBuilder<TMessage>();
        configure(builder);

        var configuration = builder.Build();
        state.Add(typeof(TMessage), configuration);

        foreach(var filterType in configuration.Filters) {
            services.TryAddTransient(filterType);
        }

        return services;
    }

    /// <summary>
    /// Registers a receive filter for a message type. Omit <paramref name="endpointName"/> to run
    /// the filter for every endpoint that consumes the type; provide it to scope the filter to one
    /// configured endpoint. Registration order is invocation order.
    /// </summary>
    /// <typeparam name="TMessage">The message type the filter applies to.</typeparam>
    /// <typeparam name="TFilter">The filter implementation; resolved from DI per delivery.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="endpointName">The endpoint name, or <see langword="null"/> for a global filter.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddReceiveFilter<TMessage, TFilter>(
        this IServiceCollection services,
        string? endpointName = null)
        where TFilter : class, IReceiveFilter<TMessage> {
        Guard.NotNull(services);

        if(endpointName is not null && StringCompat.IsNullOrWhiteSpace(endpointName)) {
            throw new ArgumentException("An endpoint-scoped receive filter requires a non-empty endpoint name.", nameof(endpointName));
        }

        var state = GetOrAddEndpointState(services);
        state.AddFilter(typeof(TMessage), endpointName, typeof(TFilter));
        services.TryAddTransient<TFilter>();

        return services;
    }

    /// <summary>
    /// Registers a consumer middleware that wraps the handler for every inbound delivery.
    /// Middleware runs in registration order; implementations are resolved once, so they must be
    /// singleton-safe or depend only on singletons.
    /// </summary>
    /// <typeparam name="TMiddleware">The middleware implementation.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddReceiveMiddleware<TMiddleware>(this IServiceCollection services)
        where TMiddleware : class, IReceiveMiddleware {
        Guard.NotNull(services);

        _ = GetOrAddEndpointState(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReceiveMiddleware, TMiddleware>());

        return services;
    }

    /// <summary>
    /// Ensures the transport-neutral receive pipeline, endpoint registry, and startup validation
    /// are registered. Transport packages call this automatically; it is public for custom
    /// adapters that call <see cref="ReceiveFilterPipeline"/> directly.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsReceivePipeline(this IServiceCollection services) {
        Guard.NotNull(services);

        _ = GetOrAddEndpointState(services);

        return services;
    }

    private static EndpointConfigurationState GetOrAddEndpointState(IServiceCollection services) {
        var descriptor = services.FirstOrDefault(static candidate => candidate.ServiceType == typeof(EndpointConfigurationState));

        if(descriptor?.ImplementationInstance is EndpointConfigurationState existing) {
            return existing;
        }

        var state = new EndpointConfigurationState();
        services.AddSingleton(state);
        services.AddCqrsMessageSerialization();
        services.TryAddSingleton(static provider => {
            var registered = provider.GetRequiredService<EndpointConfigurationState>();

            return new EndpointConfigurationRegistry(registered.Configurations, registered.Filters);
        });
        services.TryAddSingleton<ReceiveFilterPipeline>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, EndpointConfigurationHostedService>());

        return state;
    }
}
