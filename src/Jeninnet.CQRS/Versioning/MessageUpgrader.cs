//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Versioning;

/// <summary>
/// Strongly typed convenience base for <see cref="IMessageUpgrader"/>.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
public abstract class MessageUpgrader<TMessage> : IMessageUpgrader {
    /// <inheritdoc />
    public Type MessageType => typeof(TMessage);

    /// <inheritdoc />
    public abstract int FromVersion { get; }

    /// <inheritdoc />
    public abstract int ToVersion { get; }

    /// <inheritdoc />
    public object Upgrade(object message) {
        Guard.NotNull(message);

        return UpgradeCore((TMessage)message)!;
    }

    /// <summary>Upgrades the typed message.</summary>
    /// <param name="message">The message at <see cref="FromVersion"/>.</param>
    protected abstract TMessage UpgradeCore(TMessage message);
}