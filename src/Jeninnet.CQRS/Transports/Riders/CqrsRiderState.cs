//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Mutable rider registration collected from service-collection extensions. It is frozen into a
/// <see cref="CqrsRiderRegistry"/> when the container builds the singleton.
/// </summary>
internal sealed class CqrsRiderState {
    private readonly List<CqrsRiderDefinition> _riders = [];
    private readonly Dictionary<Type, string?> _routes = [];

    internal IReadOnlyList<CqrsRiderDefinition> Definitions => _riders;

    internal IReadOnlyDictionary<Type, string?> Routes => _routes;

    internal void AddRider(string name, Func<IServiceProvider, IMessageTransport> factory) {
        Guard.NotNullOrWhiteSpace(name);
        Guard.NotNull(factory);

        if(_riders.Any(rider => string.Equals(rider.Name, name, StringComparison.Ordinal))) {
            throw new CqrsRiderConfigurationException([
                $"Rider name '{name}' is already registered. Rider names must be unique.",
            ]);
        }

        _riders.Add(new CqrsRiderDefinition(name, factory));
    }

    internal void DeclareRoute(Type messageType) {
        Guard.NotNull(messageType);

        if(_routes.ContainsKey(messageType)) {
            throw new CqrsRiderConfigurationException([
                $"A rider route for message type '{messageType.FullName}' is already declared.",
            ]);
        }

        _routes.Add(messageType, null);
    }

    internal void AssignRoute(Type messageType, string riderName) {
        Guard.NotNull(messageType);
        Guard.NotNullOrWhiteSpace(riderName);

        if(!_routes.TryGetValue(messageType, out var existing)) {
            throw new CqrsRiderConfigurationException([
                $"Rider route target '{riderName}' was assigned to message type '{messageType.FullName}' before Route<{messageType.Name}>() was called.",
            ]);
        }

        if(existing is not null) {
            throw new CqrsRiderConfigurationException([
                $"Message type '{messageType.FullName}' already has a rider route to '{existing}'.",
            ]);
        }

        _routes[messageType] = riderName;
    }
}

/// <summary>One deferred rider factory captured during service configuration.</summary>
/// <param name="Name">The unique rider name.</param>
/// <param name="Factory">Creates the transport when the registry is resolved.</param>
internal sealed record CqrsRiderDefinition(string Name, Func<IServiceProvider, IMessageTransport> Factory);

/// <summary>Service marker that prevents duplicate router aliases when several riders register.</summary>
internal sealed class CqrsRiderTransportMarker { // NOSONAR: Intentional marker type used for idempotent DI registration.
}
