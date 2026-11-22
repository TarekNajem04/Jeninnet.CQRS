//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Extensions;

/// <summary>Registration for named rider transports and their message-type routing table.</summary>
public static partial class ServiceCollectionExtensions {
    /// <summary>
    /// Registers a named rider over an existing transport instance. The rider host owns the
    /// transport lifecycle; do not also register a hosted service that starts the same instance.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="name">The unique rider name used by routes and health/metrics tags.</param>
    /// <param name="transport">The transport instance. Never <see langword="null"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsRider(
        this IServiceCollection services,
        string name,
        IMessageTransport transport) {
        Guard.NotNull(services);
        Guard.NotNull(transport);

        return services.AddCqrsRider(name, _ => transport);
    }

    /// <summary>
    /// Registers a named rider whose transport is created when the singleton registry is resolved.
    /// Use this overload when the transport needs constructor-injected dependencies.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="name">The unique rider name used by routes and health/metrics tags.</param>
    /// <param name="factory">Creates the transport from the service provider. Never <see langword="null"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsRider(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, IMessageTransport> factory) {
        Guard.NotNull(services);
        Guard.NotNullOrWhiteSpace(name);
        Guard.NotNull(factory);

        EnsureRiderServices(services);
        GetOrAddRiderState(services).AddRider(name, factory);

        return services;
    }

    /// <summary>
    /// Declares the route for <typeparamref name="TMessage"/>. The returned builder must be
    /// completed with <c>To("rider-name")</c>; a missing target fails host startup with the
    /// message type in the error.
    /// </summary>
    /// <typeparam name="TMessage">The message type sent through a rider.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <returns>The route builder.</returns>
    public static CqrsRiderRouteBuilder<TMessage> Route<TMessage>(this IServiceCollection services) {
        Guard.NotNull(services);

        EnsureRiderServices(services);
        var state = GetOrAddRiderState(services);
        state.DeclareRoute(typeof(TMessage));

        return new CqrsRiderRouteBuilder<TMessage>(services, state);
    }

    private static CqrsRiderState GetOrAddRiderState(IServiceCollection services) {
        var descriptor = services.FirstOrDefault(static candidate => candidate.ServiceType == typeof(CqrsRiderState));

        if(descriptor?.ImplementationInstance is CqrsRiderState existing) {
            return existing;
        }

        var state = new CqrsRiderState();
        services.AddSingleton(state);

        return state;
    }

    private static void EnsureRiderServices(IServiceCollection services) {
        services.AddCqrsReceivePipeline();
        services.TryAddSingleton<CqrsRiderMetrics>();
        services.TryAddSingleton(static provider => CqrsRiderRegistry.Create(provider));
        services.TryAddSingleton<CqrsRiderHost>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CqrsRiderHost>());

        if(!services.Any(static descriptor => descriptor.ServiceType == typeof(CqrsRiderTransportMarker))) {
            services.AddSingleton<CqrsRiderTransport>();
            services.AddSingleton<IMessageTransport>(static provider => provider.GetRequiredService<CqrsRiderTransport>());
            services.AddSingleton<CqrsRiderTransportMarker>();
        }
    }
}

/// <summary>
/// Completes a declared rider route with its destination. Returned by
/// <c>Route&lt;TMessage&gt;()</c>.
/// </summary>
/// <typeparam name="TMessage">The message type being routed.</typeparam>
public sealed class CqrsRiderRouteBuilder<TMessage> {
    private readonly IServiceCollection _services;
    private readonly CqrsRiderState _state;

    internal CqrsRiderRouteBuilder(IServiceCollection services, CqrsRiderState state) {
        _services = services;
        _state = state;
    }

    /// <summary>Assigns the route to a registered rider name.</summary>
    /// <param name="riderName">The destination rider name.</param>
    /// <returns>The original service collection for further registration.</returns>
    public IServiceCollection To(string riderName) {
        Guard.NotNullOrWhiteSpace(riderName);

        _state.AssignRoute(typeof(TMessage), riderName);

        return _services;
    }
}