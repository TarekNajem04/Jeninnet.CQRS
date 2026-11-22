//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Default broker-neutral naming convention: every message type gets a stable exchange, queue,
/// topic, and routing key derived from its full name. Override <see cref="Describe"/> (or derive
/// and replace the registration) to match an existing broker topology.
/// </summary>
/// <param name="prefix">The namespace prefix for generated names; defaults to <c>cqrs</c>.</param>
public class TransportNamingConvention(string prefix = "cqrs") {
    /// <summary>Gets the prefix prepended to every generated broker name.</summary>
    public string Prefix { get; } = StringCompat.IsNullOrWhiteSpace(prefix)
        ? throw new ArgumentException("The transport name prefix must not be empty.", nameof(prefix))
        : prefix;

    /// <summary>Gets the topology names for <paramref name="messageType"/>.</summary>
    /// <param name="messageType">The message type.</param>
    public virtual TransportTopology Describe(Type messageType) {
        Guard.NotNull(messageType);

        var name = GetMessageName(messageType);

        return new TransportTopology(
            $"{Prefix}.{name}",
            $"{Prefix}.{name}.queue",
            $"{Prefix}.{name}",
            name);
    }

    /// <summary>Gets the broker-safe name derived from the message type's full name.</summary>
    /// <param name="messageType">The message type.</param>
    protected virtual string GetMessageName(Type messageType) {
        var fullName = messageType.FullName ?? messageType.Name;
        var builder = new StringBuilder(fullName.Length);

        foreach(var character in fullName) {
            builder.Append(IsSafe(character) ? character : '-');
        }

        return builder.ToString();
    }

    private static bool IsSafe(char value) =>
        value is >= 'a' and <= 'z' ||
        value is >= 'A' and <= 'Z' ||
        value is >= '0' and <= '9' ||
        value is '.' or '_' or '-';
}