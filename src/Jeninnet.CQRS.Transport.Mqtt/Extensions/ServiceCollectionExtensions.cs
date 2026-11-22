//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Mqtt.Extensions;

/// <summary>Registration for the MQTT transport.</summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the MQTT transport, selects <see cref="CqrsTransportMode.External"/>, and wires the
    /// hosted service that starts/stops the client with the host.
    /// </summary>
    /// <remarks>
    /// Add at least one <see cref="IMessageTransportHandler"/> and one subscription to begin
    /// consuming. User registrations made before this call win (<c>TryAdd</c>), including a custom
    /// <see cref="MqttTopicConvention"/>, serializer, or transport implementation.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional configuration of <see cref="MqttTransportOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsMqtt(
        this IServiceCollection services,
        Action<MqttTransportOptions>? configure = null) {
        Guard.NotNull(services);

        services.AddCqrsMessageSerialization();
        services.AddCqrsReceivePipeline();
        services.AddCqrsObservers();
        services.AddOptions<MqttTransportOptions>();
        services.AddOptions<CqrsTransportOptions>();
        services.Configure<CqrsTransportOptions>(static options => options.Mode = CqrsTransportMode.External);
        services.TryAddSingleton(new MqttTopicConvention());

        if(configure is not null) {
            services.Configure(configure);
        }

        services.TryAddSingleton<IMessageTransport, MqttTransport>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, MqttTransportHostedService>());

        return services;
    }
}

/// <summary>Starts and stops the registered transport with the host lifetime.</summary>
internal sealed class MqttTransportHostedService(IMessageTransport transport) : IHostedService {
    private readonly IMessageTransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) =>
        _transport.StartAsync(cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) =>
        _transport.StopAsync(cancellationToken);
}