//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Aws.Extensions;

/// <summary>
/// Registration for the AWS SQS/SNS transport.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the AWS transport, selects <see cref="CqrsTransportMode.External"/>, wires the claim
    /// check and hosted lifecycle. Credentials come from the standard AWS SDK chain.
    /// </summary>
    /// <remarks>
    /// Add at least one subscription; user registrations made before this call win
    /// (<c>TryAdd</c>). The package targets the same frameworks as core; the
    /// <c>netstandard2.0</c> asset arrives with <c>CQRS-53</c>.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional configuration of <see cref="AwsTransportOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsAws(
        this IServiceCollection services,
        Action<AwsTransportOptions>? configure = null) {
        Guard.NotNull(services);

        services.AddCqrsMessageSerialization();
        services.AddCqrsReceivePipeline();
        services.AddCqrsObservers();
        services.AddCqrsClaimCheck();
        services.AddOptions<AwsTransportOptions>();
        services.AddOptions<CqrsTransportOptions>();
        services.Configure<CqrsTransportOptions>(static options => options.Mode = CqrsTransportMode.External);
        services.TryAddSingleton(new TransportNamingConvention());

        if(configure is not null) {
            services.Configure(configure);
        }

        services.TryAddSingleton<IMessageTransport, AwsTransport>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AwsTransportHostedService>());

        return services;
    }
}

/// <summary>Starts and stops the registered transport with the host lifetime.</summary>
internal sealed class AwsTransportHostedService(IMessageTransport transport) : IHostedService {
    private readonly IMessageTransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) =>
        _transport.StartAsync(cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) =>
        _transport.StopAsync(cancellationToken);
}
