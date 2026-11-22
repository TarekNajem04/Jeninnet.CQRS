//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Mqtt;

/// <summary>
/// The single place that turns a message type and tenant into an MQTT topic:
/// <c>{prefix}/{message-type}</c> or <c>{prefix}/{message-type}/{tenant}</c>. Topic levels are
/// sanitized so caller data can never introduce MQTT wildcards or extra levels.
/// </summary>
/// <param name="prefix">The topic root; defaults to <c>cqrs</c>.</param>
public class MqttTopicConvention(string prefix = "cqrs") {
    /// <summary>Gets the topic root.</summary>
    public string Prefix { get; } = StringCompat.IsNullOrWhiteSpace(prefix)
        ? throw new ArgumentException("The MQTT topic prefix must not be empty.", nameof(prefix))
        : SanitizeLevel(prefix.Trim(), allowDots: false);

    /// <summary>Gets the topic for a message type and optional tenant.</summary>
    /// <param name="messageType">The message type.</param>
    /// <param name="tenantId">The tenant id; <see langword="null"/> or empty omits the tenant level.</param>
    /// <returns>The MQTT topic.</returns>
    public virtual string Describe(Type messageType, string? tenantId = null) {
        Guard.NotNull(messageType);

        var topic = $"{Prefix}/{GetMessageName(messageType)}";

        return StringCompat.IsNullOrWhiteSpace(tenantId)
            ? topic
            : $"{topic}/{SanitizeLevel(tenantId.Trim(), allowDots: false)}";
    }

    /// <summary>
    /// Gets the subscription filters for a message type: the exact tenant-less topic and a
    /// one-level wildcard for tenant-specific topics.
    /// </summary>
    /// <param name="messageType">The message type.</param>
    /// <returns>The MQTT topic filters.</returns>
    public virtual IReadOnlyList<string> GetSubscriptionFilters(Type messageType) {
        var topic = Describe(messageType);

        return [topic, $"{topic}/+"];
    }

    /// <summary>Gets the broker-safe message name used as the topic level.</summary>
    /// <param name="messageType">The message type.</param>
    /// <returns>A safe topic level.</returns>
    public virtual string GetMessageName(Type messageType) {
        Guard.NotNull(messageType);

        return SanitizeLevel(messageType.FullName ?? messageType.Name, allowDots: true);
    }

    /// <summary>
    /// Reads the tenant level from a topic produced by this convention, when present.
    /// </summary>
    /// <param name="topic">The received topic.</param>
    /// <param name="messageType">The subscription's message type.</param>
    /// <param name="tenantId">The tenant level, or <see langword="null"/> for a tenant-less topic.</param>
    /// <returns><see langword="true"/> when the topic belongs to this convention for the type.</returns>
    public virtual bool TryGetTenant(string topic, Type messageType, out string? tenantId) {
        Guard.NotNullOrWhiteSpace(topic);
        Guard.NotNull(messageType);

        var baseTopic = Describe(messageType);

        if(string.Equals(topic, baseTopic, StringComparison.Ordinal)) {
            tenantId = null;

            return true;
        }

        var basePrefix = baseTopic + "/";

        if(topic.StartsWith(basePrefix, StringComparison.Ordinal)) {
            tenantId = topic.Substring(basePrefix.Length);

            return tenantId.Length > 0 && !tenantId.Contains('/');
        }

        tenantId = null;

        return false;
    }

    private static string SanitizeLevel(string value, bool allowDots) {
        var builder = new StringBuilder(value.Length);

        foreach(var character in value) {
            var safe = character is >= 'a' and <= 'z' ||
                       character is >= 'A' and <= 'Z' ||
                       character is >= '0' and <= '9' ||
                       character is '_' or '-' ||
                       (allowDots && character is '.');

            builder.Append(safe ? character : '-');
        }

        var sanitized = builder.ToString();

        return sanitized.Length is 0 ? "-" : sanitized;
    }
}
