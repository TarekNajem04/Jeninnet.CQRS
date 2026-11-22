//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Immutable snapshot of the configured receive endpoints and receive filters. Transport adapters
/// resolve their prefetch/concurrency/dead-letter policy here; the receive pipeline uses the same
/// snapshot to select and order filters.
/// </summary>
public sealed class EndpointConfigurationRegistry {
    private readonly Dictionary<Type, EndpointConfiguration> _byType;
    private readonly Dictionary<string, EndpointConfiguration> _byName;
    private readonly ReceiveFilterRegistration[] _filters;

    internal EndpointConfigurationRegistry(
        IReadOnlyDictionary<Type, EndpointConfiguration> configurations,
        IReadOnlyList<ReceiveFilterRegistration> filters) {
        _byType = configurations.ToDictionary(static pair => pair.Key, static pair => pair.Value);
        _byName = new Dictionary<string, EndpointConfiguration>(StringComparer.Ordinal);
        _filters = [.. filters];

        foreach(var configuration in configurations.Values) {
            _byName[configuration.Name] = configuration;
        }
    }

    /// <summary>Gets every configured endpoint.</summary>
    public IReadOnlyCollection<EndpointConfiguration> Configurations => _byType.Values;

    /// <summary>Gets the message types that have an explicit endpoint configuration.</summary>
    public IReadOnlyCollection<Type> MessageTypes => _byType.Keys;

    /// <summary>Finds the endpoint configured for a message type.</summary>
    /// <param name="messageType">The message type.</param>
    /// <returns>The configuration, or <see langword="null"/> when the type uses transport defaults.</returns>
    public EndpointConfiguration? Find(Type messageType) {
        Guard.NotNull(messageType);

        return _byType.TryGetValue(messageType, out var configuration) ? configuration : null;
    }

    /// <summary>Resolves an endpoint by name or fails with the missing name.</summary>
    /// <param name="endpointName">The configured endpoint name.</param>
    /// <returns>The configuration.</returns>
    /// <exception cref="EndpointConfigurationException">No endpoint has that name.</exception>
    public EndpointConfiguration GetRequired(string endpointName) {
        Guard.NotNull(endpointName);

        return _byName.TryGetValue(endpointName, out var configuration)
            ? configuration
            : throw new EndpointConfigurationException(
                endpointName,
                $"No receive endpoint named '{endpointName}' is configured.");
    }

    /// <summary>
    /// Validates every endpoint, rejects duplicate names, and verifies that every filter targets a
    /// known endpoint and the correct message type. Called once when the host starts.
    /// </summary>
    /// <exception cref="EndpointConfigurationException">Any configuration or filter is invalid.</exception>
    public void ValidateAll() {
        foreach(var pair in _byType) {
            pair.Value.Validate();

            foreach(var filterType in pair.Value.Filters) {
                ValidateFilterType(pair.Key, filterType, pair.Value.Name);
            }
        }

        var duplicates = _byType.Values
            .GroupBy(static configuration => configuration.Name, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        if(duplicates.Length > 0) {
            throw new EndpointConfigurationException(
                duplicates[0],
                $"Endpoint name '{duplicates[0]}' is configured for more than one message type. Duplicates: {string.Join(", ", duplicates)}.");
        }

        foreach(var filter in _filters) {
            if(filter.EndpointName is not null && !_byName.ContainsKey(filter.EndpointName)) {
                throw new EndpointConfigurationException(
                    filter.EndpointName,
                    $"Receive filter '{filter.FilterType.Name}' is registered for unknown endpoint '{filter.EndpointName}'.");
            }

            ValidateFilterType(filter.MessageType, filter.FilterType, filter.EndpointName ?? filter.MessageType.Name);
        }
    }

    internal IReadOnlyList<Type> GetFilters(Type messageType, string? endpointName) {
        var filters = new List<Type>();

        foreach(var filter in _filters) {
            if(filter.MessageType != messageType) {
                continue;
            }

            if(filter.EndpointName is not null && !string.Equals(filter.EndpointName, endpointName, StringComparison.Ordinal)) {
                continue;
            }

            filters.Add(filter.FilterType);
        }

        if(endpointName is not null && _byName.TryGetValue(endpointName, out var configuration)) {
            filters.AddRange(configuration.Filters);
        }

        return filters;
    }

    private static void ValidateFilterType(Type messageType, Type filterType, string endpointName) {
        var expected = typeof(IReceiveFilter<>);
        var implementsContract = filterType.IsClass &&
            filterType.GetInterfaces().Any(type =>
                type.IsGenericType &&
                type.GetGenericTypeDefinition() == expected &&
                type.GetGenericArguments()[0] == messageType);

        if(!implementsContract) {
            throw new EndpointConfigurationException(
                endpointName,
                $"Receive filter '{filterType.Name}' must implement IReceiveFilter<{messageType.Name}> for endpoint '{endpointName}'.");
        }
    }
}