//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.Extensions;

/// <summary>
/// Extension methods for registering the <see cref="IdempotencyBehavior{TRequest, TResponse}"/>.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the CQRS idempotency pipeline behavior and its dependencies to the service collection.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the CQRS services will be added.</param>
    /// <param name="configure">Optional configuration of <see cref="IdempotencyBehaviorOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsIdempotencyBehavior(this IServiceCollection services, Action<IdempotencyBehaviorOptions>? configure = null) =>
        AddCqrsIdempotencyBehavior<DefaultIdempotencyStore>(services, configure);

    /// <summary>
    /// Adds CQRS idempotency behavior and registers the specified idempotency store implementation to the service collection.
    /// </summary>
    /// <remarks>
    /// This method registers the <see cref="IIdempotencyStore"/> implementation as a singleton and configures the pipeline to use idempotency behavior for CQRS requests.
    /// It also adds in-memory caching to support idempotency operations.
    /// </remarks>
    /// <typeparam name="TStore">The type of the idempotency store to use. Must implement <see cref="IIdempotencyStore"/>.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the idempotency behavior and store are added.</param>
    /// <param name="configure">Optional configuration of <see cref="IdempotencyBehaviorOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance so that additional calls can be chained.</returns>
    public static IServiceCollection AddCqrsIdempotencyBehavior<TStore>(this IServiceCollection services, Action<IdempotencyBehaviorOptions>? configure = null) where TStore : class, IIdempotencyStore {
        Guard.NotNull(services);

        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<IdempotencyBehaviorOptions>();
        services.TryAddSingleton<IIdempotencyStore, TStore>();

        if(configure is not null) {
            services.Configure(configure);
        }

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(IdempotencyBehavior<,>));

        return services;
    }
}