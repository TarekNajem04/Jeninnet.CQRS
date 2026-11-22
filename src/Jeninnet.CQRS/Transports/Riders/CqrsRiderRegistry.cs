//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Immutable snapshot of the configured riders and message routes. <see cref="Validate"/> runs
/// once at host startup; <see cref="Resolve"/> is the send-path lookup.
/// </summary>
public sealed class CqrsRiderRegistry {
    private readonly CqrsRider[] _riders;
    private readonly Dictionary<string, CqrsRider> _byName;
    private readonly Dictionary<Type, string> _routes;
    private readonly Type[] _pendingRoutes;
    private readonly CqrsRiderRoute[] _routeViews;
    private readonly EndpointConfigurationRegistry? _endpoints;
    private readonly Lock _sync = new();

    internal CqrsRiderRegistry(
        IReadOnlyList<CqrsRider> riders,
        IReadOnlyDictionary<Type, string?> routes,
        EndpointConfigurationRegistry? endpoints = null) {
        _riders = [.. riders];
        _byName = new Dictionary<string, CqrsRider>(StringComparer.Ordinal);
        _routes = new Dictionary<Type, string>();
        var pending = new List<Type>();

        foreach(var rider in _riders) {
            if(_byName.ContainsKey(rider.Name)) {
                throw new CqrsRiderConfigurationException([
                    $"Rider name '{rider.Name}' is registered more than once.",
                ]);
            }

            _byName.Add(rider.Name, rider);
        }

        foreach(var pair in routes) {
            if(pair.Value is null) {
                pending.Add(pair.Key);
            } else {
                _routes[pair.Key] = pair.Value;
            }
        }

        _pendingRoutes = [.. pending];
        _routeViews = [.. _routes.Select(pair => new CqrsRiderRoute(pair.Key, pair.Value))];
        _endpoints = endpoints;
    }

    /// <summary>Gets every configured rider in registration order.</summary>
    public IReadOnlyList<CqrsRider> Riders => _riders;

    /// <summary>Gets every fully resolved message-type route.</summary>
    public IReadOnlyCollection<CqrsRiderRoute> Routes => _routeViews;

    /// <summary>Finds a rider by name.</summary>
    /// <param name="name">The rider name.</param>
    /// <returns>The rider, or <see langword="null"/> when no rider has that name.</returns>
    public CqrsRider? Find(string name) {
        Guard.NotNullOrWhiteSpace(name);

        return _byName.TryGetValue(name, out var rider) ? rider : null;
    }

    /// <summary>Resolves the rider for a message type.</summary>
    /// <param name="messageType">The runtime message type.</param>
    /// <returns>The destination rider.</returns>
    /// <exception cref="CqrsRiderRoutingException">No route is configured for the type.</exception>
    public CqrsRider Resolve(Type messageType) {
        Guard.NotNull(messageType);

        if(_routes.TryGetValue(messageType, out var riderName) && _byName.TryGetValue(riderName, out var rider)) {
            return rider;
        }

        throw new CqrsRiderRoutingException(
            messageType,
            $"No rider route is configured for message type '{messageType.FullName}'. Call Route<{messageType.Name}>().To(\"rider-name\") during registration.");
    }

    /// <summary>Captures the current lifecycle state of every rider.</summary>
    /// <returns>The snapshots in registration order.</returns>
    public IReadOnlyList<CqrsRiderSnapshot> Snapshot() {
        lock(_sync) {
            return [.. _riders.Select(static rider => new CqrsRiderSnapshot(rider.Name, rider.Status, rider.Transport.GetType().Name))];
        }
    }

    /// <summary>
    /// Validates names, route targets, incomplete routes, and routes for configured receive
    /// endpoints. Called by the rider host before any transport starts.
    /// </summary>
    /// <exception cref="CqrsRiderConfigurationException">One or more findings were found.</exception>
    public void Validate() {
        var findings = new List<string>();

        foreach(var duplicate in _riders
            .GroupBy(static rider => rider.Name, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)) {
            findings.Add($"Rider name '{duplicate.Key}' is registered more than once.");
        }

        foreach(var messageType in _pendingRoutes) {
            findings.Add($"No rider route is configured for message type '{messageType.FullName}'; call Route<{messageType.Name}>().To(\"rider-name\").");
        }

        foreach(var pair in _routes) { // NOSONAR: Explicit loop is clearer and faster than a LINQ pipeline here.
            if(!_byName.ContainsKey(pair.Value)) {
                findings.Add($"Message type '{pair.Key.FullName}' routes to unknown rider '{pair.Value}'. Registered riders: {string.Join(", ", _byName.Keys)}.");
            }
        }

        if(_endpoints is not null) {
            foreach(var messageType in _endpoints.MessageTypes) { // NOSONAR: Explicit loop is clearer and faster than a LINQ pipeline here.
                if(!_routes.ContainsKey(messageType) && !_pendingRoutes.Contains(messageType)) {
                    findings.Add($"No rider route is configured for message type '{messageType.FullName}'.");
                }
            }
        }

        if(findings.Count > 0) {
            throw new CqrsRiderConfigurationException(findings);
        }
    }

    internal void SetStatus(string name, CqrsRiderStatus status) {
        lock(_sync) {
            if(_byName.TryGetValue(name, out var rider)) {
                rider.Status = status;
            }
        }
    }

    internal static CqrsRiderRegistry Create(IServiceProvider provider) {
        Guard.NotNull(provider);

        var state = provider.GetRequiredService<CqrsRiderState>();
        var endpoints = provider.GetService<EndpointConfigurationRegistry>();
        var riders = new List<CqrsRider>(state.Definitions.Count);

        foreach(var definition in state.Definitions) {
            var transport = definition.Factory(provider)
                ?? throw new CqrsRiderConfigurationException([
                    $"Rider '{definition.Name}' did not create a transport.",
                ]);

            riders.Add(new CqrsRider(definition.Name, transport));
        }

        return new CqrsRiderRegistry(riders, state.Routes, endpoints);
    }
}