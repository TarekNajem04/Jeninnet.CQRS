//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.FluentValidation.Extensions;

/// <summary>
/// Extension methods for registering the <see cref="FluentValidationBehavior{TRequest, TResponse}"/>.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Registers the <see cref="FluentValidationBehavior{TRequest, TResponse}"/> in the service collection.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the CQRS services will be added.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsFluentValidationBehavior(this IServiceCollection services) {
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(FluentValidationBehavior<,>));

        return services;
    }
}