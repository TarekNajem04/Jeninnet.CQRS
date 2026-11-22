//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Observers;

/// <summary>Registration for message observers and the shared invocation helper.</summary>
public static class MessageObserverServiceCollectionExtensions {
    /// <summary>
    /// Adds the observer invoker and fault metrics. Transport packages call this automatically;
    /// call it directly before registering observers for a custom adapter.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsObservers(this IServiceCollection services) {
        Guard.NotNull(services);

        services.TryAddSingleton<MessageObserverMetrics>();
        services.TryAddSingleton<MessageObserverInvoker>();

        return services;
    }

    /// <summary>Registers a send observer by type. Observers run in registration order.</summary>
    /// <typeparam name="TObserver">The observer implementation.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddSendObserver<TObserver>(this IServiceCollection services)
        where TObserver : class, ISendObserver {
        Guard.NotNull(services);

        services.AddCqrsObservers();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISendObserver, TObserver>());

        return services;
    }

    /// <summary>Registers a publish observer by type. Observers run in registration order.</summary>
    /// <typeparam name="TObserver">The observer implementation.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddPublishObserver<TObserver>(this IServiceCollection services)
        where TObserver : class, IPublishObserver {
        Guard.NotNull(services);

        services.AddCqrsObservers();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPublishObserver, TObserver>());

        return services;
    }

    /// <summary>Registers a consume observer by type. Observers run in registration order.</summary>
    /// <typeparam name="TObserver">The observer implementation.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddConsumeObserver<TObserver>(this IServiceCollection services)
        where TObserver : class, IConsumeObserver {
        Guard.NotNull(services);

        services.AddCqrsObservers();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConsumeObserver, TObserver>());

        return services;
    }
}
