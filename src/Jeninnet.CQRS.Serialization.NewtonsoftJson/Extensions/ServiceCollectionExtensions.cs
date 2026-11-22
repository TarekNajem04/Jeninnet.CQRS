//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Serialization.NewtonsoftJson.Extensions;

/// <summary>Registration for the Newtonsoft.Json message format.</summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds Newtonsoft.Json as an additional negotiated format. The default serializer stays
    /// <c>System.Text.Json</c>; consumers select this format only when the envelope content type
    /// is <c>application/x-newtonsoft-json</c>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional configuration of <see cref="NewtonsoftJsonMessageSerializerOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsNewtonsoftJsonSerialization(
        this IServiceCollection services,
        Action<NewtonsoftJsonMessageSerializerOptions>? configure = null) {
        Guard.NotNull(services);

        services.AddCqrsMessageSerialization();
        services.AddOptions<NewtonsoftJsonMessageSerializerOptions>();

        if(configure is not null) {
            services.Configure(configure);
        }

        services.TryAddSingleton<NewtonsoftJsonMessageSerializer>();
        services.AddSingleton<MessageSerializerFormat>(static provider =>
            new MessageSerializerFormat(provider.GetRequiredService<NewtonsoftJsonMessageSerializer>()));

        return services;
    }
}