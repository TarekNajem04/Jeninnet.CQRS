//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Versioning;

/// <summary>
/// Upgrades a stored message payload from one schema version to the next. Register upgraders in
/// DI; the deferred and dead-letter stores apply the chain when reading durable rows.
/// </summary>
public interface IMessageUpgrader {
    /// <summary>Gets the exact message type this upgrader handles.</summary>
    Type MessageType { get; }

    /// <summary>Gets the stored version this upgrader accepts.</summary>
    int FromVersion { get; }

    /// <summary>Gets the version this upgrader produces; must be greater than <see cref="FromVersion"/>.</summary>
    int ToVersion { get; }

    /// <summary>Upgrades <paramref name="message"/>; the returned instance feeds the next step.</summary>
    /// <param name="message">The deserialized message at <see cref="FromVersion"/>.</param>
    object Upgrade(object message);
}