//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tenancy.Extensions;

/// <summary>Registration for tenant-aware dispatch.</summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the ambient tenant context, a resolver, tenant-qualified partition keys, and strict
    /// missing-tenant enforcement. A transport, cache, idempotency store, or scheduler registered
    /// in the same provider picks the tenant up automatically.
    /// </summary>
    /// <remarks>
    /// The host is responsible for opening the scope, typically in middleware or a consumer:
    /// <code>
    /// var tenant = resolver.Resolve(scope.ServiceProvider);
    /// using var scope = tenantContext.BeginScope(tenant);
    /// await next();
    /// </code>
    /// The default is strict (<see cref="MultiTenancyOptions.RequireTenant"/> is
    /// <see langword="true"/>); missing tenants throw instead of sharing a global partition.
    /// </remarks>
    /// <typeparam name="TResolver">The resolver implementation.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional configuration of <see cref="MultiTenancyOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsMultiTenancy<TResolver>(
        this IServiceCollection services,
        Action<MultiTenancyOptions>? configure = null)
        where TResolver : class, ITenantResolver {
        Guard.NotNull(services);

        services.AddOptions<MultiTenancyOptions>();
        services.TryAddSingleton<ITenantContext, AsyncLocalTenantContext>();
        services.TryAddSingleton<ITenantScopedKey, TenantScopedKey>();
        services.TryAddSingleton<TenantAccessor>();
        services.TryAddSingleton<ITenantResolver, TResolver>();

        if(configure is not null) {
            services.Configure(configure);
        }

        return services;
    }

    /// <summary>
    /// Adds tenant-aware dispatch with a delegate resolver.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="resolver">The tenant resolver delegate.</param>
    /// <param name="configure">Optional configuration of <see cref="MultiTenancyOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsMultiTenancy(
        this IServiceCollection services,
        TenantResolver resolver,
        Action<MultiTenancyOptions>? configure = null) {
        Guard.NotNull(resolver);

        return services.AddCqrsMultiTenancy<DelegateTenantResolver>(configure)
                       .Replace(ServiceDescriptor.Singleton<ITenantResolver>(new DelegateTenantResolver(resolver)));
    }
}
