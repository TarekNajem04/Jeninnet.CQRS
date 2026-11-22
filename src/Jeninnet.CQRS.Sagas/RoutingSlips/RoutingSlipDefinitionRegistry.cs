//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.RoutingSlips;

/// <summary>
/// Immutable registry of configured routing-slip definitions. Every definition name and arguments
/// type must be unique so execution and resume are unambiguous; duplicates fail at startup.
/// </summary>
public sealed class RoutingSlipDefinitionRegistry {
    private readonly IRoutingSlipDefinition[] _definitions;
    private readonly Dictionary<Type, IRoutingSlipDefinition> _byType;
    private readonly Dictionary<string, IRoutingSlipDefinition> _byName;

    /// <summary>Initializes the registry from the registered definitions.</summary>
    /// <param name="definitions">The configured definitions.</param>
    /// <exception cref="InvalidOperationException">Two definitions share a name or arguments type.</exception>
    public RoutingSlipDefinitionRegistry(IEnumerable<IRoutingSlipDefinition> definitions) {
        _definitions = (definitions ?? throw new ArgumentNullException(nameof(definitions))).ToArray();
        _byType = new Dictionary<Type, IRoutingSlipDefinition>();
        _byName = new Dictionary<string, IRoutingSlipDefinition>(StringComparer.Ordinal);

        foreach(var definition in _definitions) {
            if(_byName.ContainsKey(definition.Name)) {
                throw new InvalidOperationException($"Routing-slip definition name '{definition.Name}' is registered more than once.");
            }

            if(_byType.ContainsKey(definition.ArgumentsType)) {
                throw new InvalidOperationException(
                    $"Routing-slip arguments type '{definition.ArgumentsType.FullName}' is used by more than one definition.");
            }

            _byName.Add(definition.Name, definition);
            _byType.Add(definition.ArgumentsType, definition);
        }
    }

    /// <summary>Gets every configured definition.</summary>
    public IReadOnlyList<IRoutingSlipDefinition> Definitions => _definitions;

    /// <summary>Resolves the definition for an arguments type.</summary>
    /// <param name="argumentsType">The strongly typed arguments type.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="InvalidOperationException">No definition uses that arguments type.</exception>
    public IRoutingSlipDefinition GetRequired(Type argumentsType) {
        Guard.NotNull(argumentsType);

        return _byType.TryGetValue(argumentsType, out var definition)
            ? definition
            : throw new InvalidOperationException(
                $"No routing-slip definition is registered for arguments type '{argumentsType.FullName}'.");
    }

    /// <summary>Resolves a definition by its stable name.</summary>
    /// <param name="name">The definition name.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="InvalidOperationException">No definition has that name.</exception>
    public IRoutingSlipDefinition GetRequired(string name) {
        Guard.NotNullOrWhiteSpace(name);

        return _byName.TryGetValue(name, out var definition)
            ? definition
            : throw new InvalidOperationException($"No routing-slip definition named '{name}' is registered.");
    }
}