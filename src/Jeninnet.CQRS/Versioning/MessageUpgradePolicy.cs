//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Versioning;

/// <summary>
/// Applies the registered <see cref="IMessageUpgrader"/> chain to durable payloads. Without
/// upgraders every message is version 1 and reads unchanged; rows below the current version are
/// upgraded step by step, and unknown versions fail loudly for the dead-letter path.
/// </summary>
public sealed class MessageUpgradePolicy {
    private readonly Dictionary<Type, SortedDictionary<int, IMessageUpgrader>> _chains = [];

    /// <summary>
    /// Initializes the policy from the registered upgraders.
    /// </summary>
    /// <param name="upgraders">Registered upgraders; an empty sequence means version 1 only.</param>
    /// <exception cref="ArgumentException">
    /// Thrown for a non-increasing version step or two upgraders starting from the same version.
    /// </exception>
    public MessageUpgradePolicy(IEnumerable<IMessageUpgrader>? upgraders = null) {
        foreach(var upgrader in upgraders ?? []) {
            Guard.NotNull(upgrader);

            if(upgrader.ToVersion <= upgrader.FromVersion) {
                throw new ArgumentException(
                    $"Upgrader for '{upgrader.MessageType.Name}' must produce a version greater than {upgrader.FromVersion}.",
                    nameof(upgraders));
            }

            if(!_chains.TryGetValue(upgrader.MessageType, out var chain)) {
                chain = [];
                _chains.Add(upgrader.MessageType, chain);
            }

            if(chain.ContainsKey(upgrader.FromVersion)) {
                throw new ArgumentException(
                    $"A second upgrader for '{upgrader.MessageType.Name}' from version {upgrader.FromVersion} is registered.",
                    nameof(upgraders));
            }

            chain.Add(upgrader.FromVersion, upgrader);
        }
    }

    /// <summary>Gets the highest version the reader supports for <paramref name="messageType"/>.</summary>
    /// <param name="messageType">The message type.</param>
    public int CurrentVersion(Type messageType) {
        Guard.NotNull(messageType);

        return _chains.TryGetValue(messageType, out var chain) && chain.Count > 0
            ? chain.Values.Max(static upgrader => upgrader.ToVersion)
            : 1;
    }

    /// <summary>Gets whether <paramref name="version"/> can be read for <paramref name="messageType"/>.</summary>
    /// <param name="messageType">The message type.</param>
    /// <param name="version">The stored version (zero/null legacy rows mean version 1).</param>
    public bool IsKnownVersion(Type messageType, int version) =>
        version <= CurrentVersion(messageType) && version >= 1;

    /// <summary>
    /// Upgrades <paramref name="message"/> from <paramref name="storedVersion"/> to the current
    /// version by applying the chain in order.
    /// </summary>
    /// <param name="message">The deserialized message.</param>
    /// <param name="messageType">The message type.</param>
    /// <param name="storedVersion">The row version; zero or negative means version 1.</param>
    /// <exception cref="UnknownMessageVersionException">
    /// The row is newer than the reader or the chain has a gap.
    /// </exception>
    public object Upgrade(object message, Type messageType, int storedVersion) {
        Guard.NotNull(message);
        Guard.NotNull(messageType);

        var version = storedVersion < 1 ? 1 : storedVersion;
        var current = CurrentVersion(messageType);

        if(version > current) {
            throw new UnknownMessageVersionException(messageType, version, current);
        }

        while(version < current) {
            if(!_chains.TryGetValue(messageType, out var chain) || !chain.TryGetValue(version, out var upgrader)) {
                throw new UnknownMessageVersionException(messageType, version, current);
            }

            message = upgrader.Upgrade(message);
            version = upgrader.ToVersion;
        }

        return message;
    }
}
