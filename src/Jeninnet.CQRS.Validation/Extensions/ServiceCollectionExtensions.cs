//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Validation.Extensions;

/// <summary>
/// Extension methods for registering the <see cref="ValidationBehavior{TRequest, TResponse}"/>.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Registers the <see cref="ValidationBehavior{TRequest, TResponse}"/> in the service collection.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the CQRS services will be added.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsValidationBehavior(this IServiceCollection services) =>
        services.AddCqrsValidationBehavior(null);

    /// <summary>
    /// Registers the <see cref="ValidationBehavior{TRequest, TResponse}"/> with the given
    /// options (for example a result adapter that turns failures into values).
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the CQRS services will be added.</param>
    /// <param name="configure">Configures <see cref="CqrsValidationOptions"/>; <see langword="null"/> keeps the throwing default.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsValidationBehavior(this IServiceCollection services, Action<CqrsValidationOptions>? configure) {
        services.AddOptions<CqrsValidationOptions>();

        if(configure is not null) {
            services.Configure(configure);
        }

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        return services;
    }
}