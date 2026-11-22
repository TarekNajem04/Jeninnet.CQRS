//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Extensions;

/// <summary>Registration for retention policies, the hosted sweeper, and its instruments.</summary>
public static partial class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the hosted retention sweeper. Store adapters are registered separately (for example by
    /// <c>AddCqrsEfRetention</c>); a store without a policy is never touched.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Configures the interval, batch size, lease, dry-run flag, and policies.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsRetention(
        this IServiceCollection services,
        Action<RetentionOptions>? configure = null) {
        Guard.NotNull(services);

        services.AddOptions<RetentionOptions>();
        services.TryAddSingleton(TimeProvider.System);

        if(configure is not null) {
            services.Configure(configure);
        }

        services.TryAddSingleton<IRetentionLease, InMemoryRetentionLease>();
        services.TryAddSingleton<RetentionMetrics>();
        services.TryAddSingleton<RetentionSweeper>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RetentionSweeper>());

        return services;
    }
}
