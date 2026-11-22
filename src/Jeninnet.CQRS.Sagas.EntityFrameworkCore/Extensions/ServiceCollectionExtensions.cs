//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.EntityFrameworkCore.Extensions;

/// <summary>Registration for durable EF Core saga persistence.</summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds a saga machine and replaces the in-memory store with <see cref="EfSagaStore"/>.
    /// </summary>
    /// <remarks>
    /// Call this after registering the core machine (it calls <c>AddCqrsSagas&lt;TSaga&gt;()</c>
    /// itself). Create the schema with <c>EnsureCreatedAsync</c> or the SQL in the package
    /// README. Without <paramref name="configureDb"/>, register
    /// <see cref="IDbContextFactory{SagaDbContext}"/> yourself first.
    /// </remarks>
    /// <typeparam name="TSaga">The machine type.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configureDb">Registers the <see cref="SagaDbContext"/> factory when given.</param>
    /// <param name="configure">Optional configuration of <see cref="SagaOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsEfSagas<TSaga>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDb = null,
        Action<SagaOptions>? configure = null)
        where TSaga : class, ISaga {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCqrsSagas<TSaga>(configure);

        if(configureDb is not null) {
            services.AddDbContextFactory<SagaDbContext>(configureDb);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ISagaStore, EfSagaStore>();

        return services;
    }

    /// <summary>
    /// Adds the durable saga retention adapter and the core hosted sweeper. Only terminal sagas
    /// older than the configured policy are archived; active sagas are never touched.
    /// </summary>
    /// <remarks>
    /// Register the saga machine with <c>AddCqrsSagas</c> or <c>AddCqrsEfSagas</c> first. Policies
    /// are explicit and dry-run is the core default. Create the archive table with
    /// <c>EnsureCreatedAsync</c> or the package README schema.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configureDb">Registers the <see cref="SagaDbContext"/> factory when given.</param>
    /// <param name="configure">Configures the interval, batch, dry-run flag, and saga policy.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsEfSagaRetention(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDb = null,
        Action<RetentionOptions>? configure = null) {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCqrsRetention(configure);

        if(configureDb is not null) {
            services.AddDbContextFactory<SagaDbContext>(configureDb);
        }

        services.AddSingleton<IRetentionStore, EfSagaRetentionStore>();

        return services;
    }

    /// <summary>
    /// Replaces the in-memory routing-slip store with <see cref="EfRoutingSlipStore"/>. Register
    /// definitions with <c>AddRoutingSlip&lt;TArguments&gt;(...)</c>; this call can come before or
    /// after them because the durable store is the last registration.
    /// </summary>
    /// <remarks>
    /// The rows live in <see cref="SagaDbContext"/> beside the saga table. Create the schema with
    /// <c>EnsureCreatedAsync</c>. Without <paramref name="configureDb"/>, register
    /// <see cref="IDbContextFactory{SagaDbContext}"/> yourself first.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configureDb">Registers the <see cref="SagaDbContext"/> factory when given.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsEfRoutingSlips(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDb = null) {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCqrsRoutingSlips();

        if(configureDb is not null) {
            services.AddDbContextFactory<SagaDbContext>(configureDb);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IRoutingSlipStore, EfRoutingSlipStore>();

        return services;
    }
}
