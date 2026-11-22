//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.RabbitMq.Extensions;

/// <summary>
/// Registration for the RabbitMQ transport.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the RabbitMQ transport, selects <see cref="CqrsTransportMode.External"/>, and wires
    /// the hosted service that starts/stops the connection with the host.
    /// </summary>
    /// <remarks>
    /// The application must add at least one <see cref="IMessageTransportHandler"/> and one
    /// subscription to <see cref="RabbitMqTransportOptions.Subscriptions"/> to consume messages.
    /// User registrations made before this call win (<c>TryAdd</c>): a custom naming convention,
    /// serializer, or transport implementation is preserved. The package currently targets the
    /// same frameworks as the core package; the <c>netstandard2.0</c> asset arrives with
    /// <c>CQRS-53</c> when core gains that target.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional configuration of <see cref="RabbitMqTransportOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsRabbitMq(
        this IServiceCollection services,
        Action<RabbitMqTransportOptions>? configure = null) {
        Guard.NotNull(services);

        services.AddCqrsMessageSerialization();
        services.AddCqrsReceivePipeline();
        services.AddCqrsObservers();
        services.AddOptions<RabbitMqTransportOptions>();
        services.AddOptions<CqrsTransportOptions>();
        services.Configure<CqrsTransportOptions>(static options => options.Mode = CqrsTransportMode.External);
        services.TryAddSingleton(new TransportNamingConvention());

        if(configure is not null) {
            services.Configure(configure);
        }

        services.TryAddSingleton<IMessageTransport, RabbitMqTransport>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RabbitMqTransportHostedService>());

        return services;
    }
}

/// <summary>Starts and stops the registered transport with the host lifetime.</summary>
internal sealed class RabbitMqTransportHostedService(IMessageTransport transport) : IHostedService {
    private readonly IMessageTransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) =>
        _transport.StartAsync(cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) =>
        _transport.StopAsync(cancellationToken);
}