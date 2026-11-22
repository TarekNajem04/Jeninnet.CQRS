//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.AspNetCore.Extensions;

/// <summary>
/// Extension methods for registering the minimal-API endpoint helpers.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the options used by the CQRS endpoint helpers (for example a custom exception
    /// mapping). No services other than the option infrastructure are registered.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional configuration of <see cref="CqrsEndpointOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsEndpoints(this IServiceCollection services, Action<CqrsEndpointOptions>? configure = null) {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<CqrsEndpointOptions>();

        if(configure is not null) {
            services.Configure(configure);
        }

        return services;
    }
}
