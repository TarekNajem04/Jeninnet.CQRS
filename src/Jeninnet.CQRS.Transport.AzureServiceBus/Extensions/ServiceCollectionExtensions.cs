//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.AzureServiceBus.Extensions;

/// <summary>
/// Registration for the Azure Service Bus transport.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the Azure Service Bus transport, selects <see cref="CqrsTransportMode.External"/>, and
    /// wires the hosted service that starts/stops senders and receivers with the host.
    /// </summary>
    /// <remarks>
    /// The core dead-letter store is registered too, so handler failures leave an inspectable copy
    /// with the original headers. Add at least one subscription; user registrations made before
    /// this call win (<c>TryAdd</c>). The package targets the same frameworks as core; the
    /// <c>netstandard2.0</c> asset arrives with <c>CQRS-53</c>.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional configuration of <see cref="AzureServiceBusTransportOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsAzureServiceBus(
        this IServiceCollection services,
        Action<AzureServiceBusTransportOptions>? configure = null) {
        Guard.NotNull(services);

        services.AddCqrsMessageSerialization();
        services.AddCqrsReceivePipeline();
        services.AddCqrsObservers();
        services.AddCqrsDeadLetters();
        services.AddOptions<AzureServiceBusTransportOptions>();
        services.AddOptions<CqrsTransportOptions>();
        services.Configure<CqrsTransportOptions>(static options => options.Mode = CqrsTransportMode.External);
        services.TryAddSingleton(new TransportNamingConvention());

        if(configure is not null) {
            services.Configure(configure);
        }

        services.TryAddSingleton<IMessageTransport, AzureServiceBusTransport>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AzureServiceBusTransportHostedService>());

        return services;
    }
}

/// <summary>Starts and stops the registered transport with the host lifetime.</summary>
internal sealed class AzureServiceBusTransportHostedService(IMessageTransport transport) : IHostedService {
    private readonly IMessageTransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) =>
        _transport.StartAsync(cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) =>
        _transport.StopAsync(cancellationToken);
}
