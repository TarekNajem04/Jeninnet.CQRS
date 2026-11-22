//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Caching.Extensions;

/// <summary>
/// Extension methods for registering the in-memory and distributed caching behaviors.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the in-memory <see cref="IMemoryCache"/> caching behavior.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsCachingBehavior(this IServiceCollection services) {
        Guard.NotNull(services);

        services.AddMemoryCache();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));

        return services;
    }

    /// <summary>
    /// Adds the distributed caching behavior over an <see cref="IDistributedCache"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A process-local memory distributed cache is registered only when the host supplied none,
    /// so single-instance hosts work out of the box while multi-instance hosts register Redis,
    /// SQL, or another provider themselves.
    /// </para>
    /// <para>
    /// The default message serializer (JSON) is registered through
    /// <c>AddCqrsMessageSerialization</c>; register a custom one to replace it. Cache failures
    /// never fail requests; warnings are throttled by <see cref="DistributedCacheOptions.FailureWindow"/>.
    /// </para>
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional configuration of <see cref="DistributedCacheOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsDistributedCaching(this IServiceCollection services, Action<DistributedCacheOptions>? configure = null) {
        Guard.NotNull(services);

        services.AddDistributedMemoryCache();
        services.AddCqrsMessageSerialization();
        services.AddOptions<DistributedCacheOptions>();

        if(configure is not null) {
            services.Configure(configure);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<CacheFailureReporter>();
        services.TryAddSingleton<CacheStampedeGuard>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(DistributedCachingBehavior<,>));

        return services;
    }
}
