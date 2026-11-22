//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.Extensions;

/// <summary>Registration for saga machines and the in-memory dispatcher.</summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds a saga machine and the notification observer that drives it. The in-memory store is
    /// the default; add <c>AddCqrsEfSagas&lt;TSaga&gt;()</c> from the EF package to make state
    /// restart-safe.
    /// </summary>
    /// <remarks>
    /// Call this for every machine. The host still needs the normal CQRS dispatchers
    /// (<c>AddCqrs()</c> or explicit registrations) because saga follow-ups run through
    /// <see cref="ICqrsBroker"/>. Saga configuration is validated at host startup; duplicate
    /// ownership of one event type fails fast.
    /// </remarks>
    /// <typeparam name="TSaga">The machine type.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional configuration of <see cref="SagaOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsSagas<TSaga>(
        this IServiceCollection services,
        Action<SagaOptions>? configure = null)
        where TSaga : class, ISaga {
        Guard.NotNull(services);

        services.AddCqrsMessageSerialization();
        services.AddCqrsNotifications();
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<SagaOptions>();

        if(configure is not null) {
            services.Configure(configure);
        }

        services.AddCqrsObservers();
        services.TryAddSingleton<ISagaStore, InMemorySagaStore>();
        services.TryAddSingleton<SagaMetrics>();
        services.TryAddSingleton<SagaObserverDispatcher>();
        services.TryAddSingleton<SagaDispatcher>();

        if(!services.Any(static descriptor => descriptor.ServiceType == typeof(SagaObserverMarker))) {
            services.AddSingleton<INotificationObserver>(static provider => provider.GetRequiredService<SagaDispatcher>());
            services.AddSingleton<SagaObserverMarker>();
        }

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SagaDispatcherHostedService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SagaMetricsHostedService>());
        services.TryAddSingleton<TSaga>();
        services.AddSingleton<ISaga>(static provider => provider.GetRequiredService<TSaga>());
        services.TryAddTransient<ICommandHandler<SagaTimeoutCommand>, SagaTimeoutCommandHandler>();

        return services;
    }

    /// <summary>Registers a saga observer. Observers run in registration order.</summary>
    /// <typeparam name="TObserver">The observer implementation.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsSagaObserver<TObserver>(this IServiceCollection services)
        where TObserver : class, ISagaObserver {
        Guard.NotNull(services);

        services.AddCqrsObservers();
        services.TryAddSingleton<SagaObserverDispatcher>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISagaObserver, TObserver>());

        return services;
    }

    /// <summary>Marker that keeps the saga observer registration idempotent across machines.</summary>
    internal sealed class SagaObserverMarker; // NOSONAR: Intentional marker type used for idempotent DI registration.
}
