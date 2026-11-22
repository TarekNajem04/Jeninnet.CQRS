//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Mutable registration state collected from <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>
/// extensions. It is read once when the container builds the immutable
/// <see cref="EndpointConfigurationRegistry"/>.
/// </summary>
internal sealed class EndpointConfigurationState {
    public Dictionary<Type, EndpointConfiguration> Configurations { get; } = [];

    public List<ReceiveFilterRegistration> Filters { get; } = [];

    public void Add(Type messageType, EndpointConfiguration configuration) {
        if(Configurations.ContainsKey(messageType)) {
            throw new EndpointConfigurationException(
                configuration.Name,
                $"An endpoint is already configured for message type '{messageType.FullName}'.");
        }

        Configurations[messageType] = configuration;
    }

    public void AddFilter(Type messageType, string? endpointName, Type filterType) =>
        Filters.Add(new ReceiveFilterRegistration(messageType, endpointName, filterType));
}
