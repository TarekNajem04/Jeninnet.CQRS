//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Jobs;

/// <summary>Registration for the job queue, worker, handlers, metrics, and retention pruner.</summary>
public static class JobServiceCollectionExtensions {
    /// <summary>
    /// Adds the job queue, status client, worker, in-memory store, metrics, and retention pruner.
    /// Call it once before registering handlers; the EF Core package replaces the store with
    /// <c>AddCqrsEfJobs</c>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional configuration of <see cref="JobOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsJobs(
        this IServiceCollection services,
        Action<JobOptions>? configure = null) {
        Guard.NotNull(services);

        services.AddCqrsMessageSerialization();
        services.AddCqrsDeadLetters();
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<JobOptions>();

        if(configure is not null) {
            services.Configure(configure);
        }

        services.TryAddSingleton<InMemoryJobStore>();
        services.TryAddSingleton<IJobStore>(static provider => provider.GetRequiredService<InMemoryJobStore>());
        services.TryAddSingleton<JobHandlerRegistry>();
        services.TryAddSingleton<JobMetrics>();
        services.TryAddSingleton<IJobQueue, JobQueue>();
        services.TryAddSingleton<IJobClient, JobClient>();
        services.TryAddSingleton<JobWorker>();

        if(!services.Any(static descriptor => descriptor.ServiceType == typeof(JobWorkerHostedMarker))) {
            services.AddSingleton<IHostedService>(static provider => provider.GetRequiredService<JobWorker>());
            services.AddSingleton<JobWorkerHostedMarker>();
        }

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, JobPruningService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, JobMetricsHostedService>());

        return services;
    }

    /// <summary>
    /// Registers one strongly typed job handler. The job type must be unique; duplicates fail at
    /// host startup when the handler registry is resolved.
    /// </summary>
    /// <typeparam name="TJob">The job payload type.</typeparam>
    /// <typeparam name="THandler">The handler implementation.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddJobHandler<TJob, THandler>(this IServiceCollection services)
        where TJob : class
        where THandler : class, IJobHandler<TJob> {
        Guard.NotNull(services);

        services.AddCqrsJobs();
        services.TryAddTransient<THandler>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandlerInvoker, JobHandlerInvoker<TJob, THandler>>());

        return services;
    }
}
