//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Extensions;

/// <summary>
/// Extension methods for registering transports.
/// </summary>
public static partial class ServiceCollectionExtensions {
    /// <summary>
    /// Registers the in-memory transport used by tests and local development. It serializes
    /// through the configured <see cref="IMessageSerializer"/> and delivers envelopes to every
    /// registered <see cref="IMessageTransportHandler"/>; no broker is involved.
    /// </summary>
    /// <remarks>
    /// This selects <see cref="CqrsTransportMode.InMemory"/> in <see cref="CqrsTransportOptions"/>.
    /// Without this call the default remains <see cref="CqrsTransportMode.InProcess"/>, which keeps
    /// the existing in-process relay and outbox behavior. User registrations of
    /// <see cref="IMessageTransport"/>, <see cref="TransportNamingConvention"/>, or
    /// <see cref="IMessageSerializer"/> made before this call win.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsInMemoryTransport(this IServiceCollection services) {
        Guard.NotNull(services);

        services.AddCqrsMessageSerialization();
        services.AddCqrsReceivePipeline();
        services.AddCqrsObservers();
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<CqrsTransportOptions>();
        services.Configure<CqrsTransportOptions>(static options => options.Mode = CqrsTransportMode.InMemory);
        services.TryAddSingleton(new TransportNamingConvention());
        services.TryAddSingleton<IMessageTransport, InMemoryTransport>();

        return services;
    }
}