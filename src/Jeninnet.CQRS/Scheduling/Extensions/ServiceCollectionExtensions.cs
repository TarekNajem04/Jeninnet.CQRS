//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Extensions;

/// <summary>Registration for the persistent message scheduler.</summary>
public static partial class ServiceCollectionExtensions {
    /// <summary>
    /// Adds delayed/one-shot/recurring scheduling over <see cref="Scheduling.IMessageScheduler"/>, a
    /// persistent in-memory store by default, a hosted due-scan worker, and scheduler metrics.
    /// </summary>
    /// <remarks>
    /// A transport must be registered before the host starts; each due row is published through
    /// <see cref="Transports.IMessageTransport"/>. Replace the store with the EF implementation
    /// from the idempotency package for restart safety. Cron expressions are five-field only.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional configuration of <see cref="Scheduling.SchedulerOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsScheduler(
        this IServiceCollection services,
        Action<Scheduling.SchedulerOptions>? configure = null) {
        Guard.NotNull(services);

        services.AddCqrsMessageSerialization();
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<Scheduling.SchedulerOptions>();

        if(configure is not null) {
            services.Configure(configure);
        }

        services.TryAddSingleton<Scheduling.ISchedulerStore, Scheduling.InMemorySchedulerStore>();
        services.TryAddSingleton<Scheduling.IMessageScheduler, Scheduling.MessageScheduler>();
        services.TryAddSingleton<Scheduling.SchedulerMetrics>();
        services.TryAddSingleton<Scheduling.SchedulerWorker>();

        if(!services.Any(static descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(Scheduling.SchedulerWorker))) {
            services.AddSingleton<IHostedService>(static provider => provider.GetRequiredService<Scheduling.SchedulerWorker>());
        }

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, Scheduling.SchedulerMetricsHostedService>());

        return services;
    }
}
