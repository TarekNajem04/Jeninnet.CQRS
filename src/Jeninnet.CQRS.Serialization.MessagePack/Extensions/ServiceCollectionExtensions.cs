//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Serialization.MessagePack.Extensions;

/// <summary>Registration for the MessagePack message format.</summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds MessagePack as an additional negotiated format. The default serializer stays
    /// <c>System.Text.Json</c>; consumers select this format only when the envelope content type
    /// is <c>application/x-msgpack</c>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional configuration of <see cref="MessagePackMessageSerializerOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsMessagePackSerialization(
        this IServiceCollection services,
        Action<MessagePackMessageSerializerOptions>? configure = null) {
        Guard.NotNull(services);

        services.AddCqrsMessageSerialization();
        services.AddOptions<MessagePackMessageSerializerOptions>();

        if(configure is not null) {
            services.Configure(configure);
        }

        services.TryAddSingleton<MessagePackMessageSerializer>();
        services.AddSingleton<MessageSerializerFormat>(static provider =>
            new MessageSerializerFormat(provider.GetRequiredService<MessagePackMessageSerializer>()));

        return services;
    }
}
