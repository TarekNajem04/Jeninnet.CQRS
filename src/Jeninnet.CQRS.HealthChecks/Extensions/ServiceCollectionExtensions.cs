//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.HealthChecks.Extensions;

/// <summary>
/// Extension methods for registering the CQRS health check.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the CQRS health check, reporting dispatcher availability and open circuit breakers.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which health checks will be added.</param>
    /// <param name="name">The health check name. Defaults to <c>cqrs</c>.</param>
    /// <param name="tags">Optional tags for filtering health endpoints.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsHealthChecks(this IServiceCollection services, string name = "cqrs", IEnumerable<string>? tags = null) {
        Guard.NotNull(services);
        Guard.NotNullOrEmpty(name);

        services.AddHealthChecks()
                .AddCheck<CqrsHealthCheck>(name, tags: tags);

        return services;
    }

    /// <summary>
    /// Adds durability health checks for the stores registered at this call: <c>deferred</c>
    /// (backlog thresholds) and <c>outbox</c> (lag thresholds). Stores absent at this call are
    /// skipped entirely.
    /// </summary>
    /// <remarks>
    /// Call this after registering the stores. Deferred backlog is degraded/unhealthy at or above
    /// the configured counts; outbox health uses the age of the oldest pending notification.
    /// Store read failures are reported as unhealthy.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional configuration of <see cref="DurabilityHealthOptions"/>.</param>
    /// <param name="deferredName">The deferred health-check name. Defaults to <c>deferred</c>.</param>
    /// <param name="outboxName">The outbox health-check name. Defaults to <c>outbox</c>.</param>
    /// <param name="tags">Optional tags for filtering health endpoints.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsDurabilityHealthChecks(
        this IServiceCollection services,
        Action<DurabilityHealthOptions>? configure = null,
        string deferredName = "deferred",
        string outboxName = "outbox",
        IEnumerable<string>? tags = null) {
        Guard.NotNull(services);
        Guard.NotNullOrEmpty(deferredName);
        Guard.NotNullOrEmpty(outboxName);

        services.AddOptions<DurabilityHealthOptions>();

        if(configure is not null) {
            services.Configure(configure);
        }

        services.TryAddSingleton(TimeProvider.System);
        var builder = services.AddHealthChecks();

        if(services.Any(static descriptor => descriptor.ServiceType == typeof(IDeferredStore))) {
            builder.AddCheck<DeferredBacklogHealthCheck>(deferredName, tags: tags);
        }

        if(services.Any(static descriptor => descriptor.ServiceType == typeof(IOutboxStore))) {
            builder.AddCheck<OutboxLagHealthCheck>(outboxName, tags: tags);
        }

        return services;
    }

    /// <summary>
    /// Adds the <c>sagas</c> health contribution when an <see cref="ISagaStore"/> is registered.
    /// Active sagas older than <see cref="SagaHealthOptions.StuckAfter"/> are reported unhealthy;
    /// terminal states declared with <c>Completed()</c>/<c>Failure()</c> are ignored.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration of <see cref="SagaHealthOptions"/>.</param>
    /// <param name="name">The health-check name. Defaults to <c>sagas</c>.</param>
    /// <param name="tags">Optional tags for filtering health endpoints.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddCqrsSagaHealthChecks(
        this IServiceCollection services,
        Action<SagaHealthOptions>? configure = null,
        string name = "sagas",
        IEnumerable<string>? tags = null) {
        Guard.NotNull(services);
        Guard.NotNullOrEmpty(name);

        services.AddOptions<SagaHealthOptions>();

        if(configure is not null) {
            services.Configure(configure);
        }

        services.TryAddSingleton(TimeProvider.System);

        if(services.Any(static descriptor => descriptor.ServiceType == typeof(ISagaStore))) {
            services.AddHealthChecks().AddCheck<SagaStuckHealthCheck>(name, tags: tags);
        }

        return services;
    }

    /// <summary>
    /// Adds the <c>routing-slips</c> health contribution when an <see cref="IRoutingSlipStore"/> is
    /// registered. Running or compensating slips older than
    /// <see cref="RoutingSlipHealthOptions.StuckAfter"/> are reported unhealthy.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration of <see cref="RoutingSlipHealthOptions"/>.</param>
    /// <param name="name">The health-check name. Defaults to <c>routing-slips</c>.</param>
    /// <param name="tags">Optional tags for filtering health endpoints.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddCqrsRoutingSlipHealthChecks(
        this IServiceCollection services,
        Action<RoutingSlipHealthOptions>? configure = null,
        string name = "routing-slips",
        IEnumerable<string>? tags = null) {
        Guard.NotNull(services);
        Guard.NotNullOrEmpty(name);

        services.AddOptions<RoutingSlipHealthOptions>();

        if(configure is not null) {
            services.Configure(configure);
        }

        services.TryAddSingleton(TimeProvider.System);

        if(services.Any(static descriptor => descriptor.ServiceType == typeof(IRoutingSlipStore))) {
            services.AddHealthChecks().AddCheck<RoutingSlipStuckHealthCheck>(name, tags: tags);
        }

        return services;
    }

    /// <summary>
    /// Adds the <c>scheduler</c> due-backlog health contribution when an
    /// <see cref="ISchedulerStore"/> is registered.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration of <see cref="SchedulerHealthOptions"/>.</param>
    /// <param name="name">The health-check name. Defaults to <c>scheduler</c>.</param>
    /// <param name="tags">Optional tags for filtering health endpoints.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddCqrsSchedulerHealthChecks(
        this IServiceCollection services,
        Action<SchedulerHealthOptions>? configure = null,
        string name = "scheduler",
        IEnumerable<string>? tags = null) {
        Guard.NotNull(services);
        Guard.NotNullOrEmpty(name);

        services.AddOptions<SchedulerHealthOptions>();

        if(configure is not null) {
            services.Configure(configure);
        }

        services.TryAddSingleton(TimeProvider.System);

        if(services.Any(static descriptor => descriptor.ServiceType == typeof(ISchedulerStore))) {
            services.AddHealthChecks().AddCheck<SchedulerBacklogHealthCheck>(name, tags: tags);
        }

        return services;
    }

    /// <summary>
    /// Adds the rider lifecycle health contribution. The check reports each rider by name in its
    /// result data and is healthy when every rider is started.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The health-check name. Defaults to <c>riders</c>.</param>
    /// <param name="tags">Optional tags for filtering health endpoints.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddCqrsRiderHealthChecks(
        this IServiceCollection services,
        string name = "riders",
        IEnumerable<string>? tags = null) {
        Guard.NotNull(services);
        Guard.NotNullOrEmpty(name);

        services.AddHealthChecks().AddCheck<CqrsRiderHealthCheck>(name, tags: tags);

        return services;
    }

    /// <summary>
    /// Adds the retention sweeper freshness contribution. The check is healthy only when a sweep
    /// completed within <see cref="RetentionHealthOptions.MaxLag"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration of <see cref="RetentionHealthOptions"/>.</param>
    /// <param name="name">The health-check name. Defaults to <c>retention</c>.</param>
    /// <param name="tags">Optional tags for filtering health endpoints.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddCqrsRetentionHealthChecks(
        this IServiceCollection services,
        Action<RetentionHealthOptions>? configure = null,
        string name = "retention",
        IEnumerable<string>? tags = null) {
        Guard.NotNull(services);
        Guard.NotNullOrEmpty(name);

        services.AddOptions<RetentionHealthOptions>();
        services.TryAddSingleton(TimeProvider.System);

        if(configure is not null) {
            services.Configure(configure);
        }

        services.AddHealthChecks().AddCheck<RetentionHealthCheck>(name, tags: tags);

        return services;
    }
}