//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Mqtt;

/// <summary>
/// MQTT quality-of-service levels. The value maps directly to the MQTT protocol levels
/// (0 = at most once, 1 = at least once, 2 = exactly once between client and broker).
/// </summary>
public enum MqttQualityOfService {
    /// <summary>Fire and forget; the broker does not acknowledge delivery.</summary>
    AtMostOnce = 0,

    /// <summary>The broker acknowledges delivery; duplicates are possible after a reconnect.</summary>
    AtLeastOnce = 1,

    /// <summary>The broker completes the two-phase QoS 2 flow; handler effects remain at-least-once.</summary>
    ExactlyOnce = 2,
}

/// <summary>
/// Connection, topic, QoS, retained-message, and reconnection options for the MQTT transport.
/// </summary>
public sealed class MqttTransportOptions {
    /// <summary>Gets or sets the broker host. Defaults to <c>localhost</c>.</summary>
    public string HostName { get; set; } = "localhost";

    /// <summary>Gets or sets the MQTT port. Defaults to 1883.</summary>
    public int Port { get; set; } = 1883;

    /// <summary>
    /// Gets or sets the client id. Defaults to a generated id per transport. A stable id is
    /// required when <see cref="CleanSession"/> is <see langword="false"/> so the broker can
    /// resume the session.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>Gets or sets the optional user name.</summary>
    public string? UserName { get; set; }

    /// <summary>Gets or sets the optional password.</summary>
    public string? Password { get; set; }

    /// <summary>Gets or sets whether the TCP connection uses TLS. Defaults to <see langword="false"/>.</summary>
    public bool UseTls { get; set; }

    /// <summary>
    /// Gets or sets whether the broker starts a clean session. Defaults to <see langword="true"/>;
    /// set it to <see langword="false"/> with a stable <see cref="ClientId"/> for durable sessions.
    /// </summary>
    public bool CleanSession { get; set; } = true;

    /// <summary>Gets or sets the keep-alive interval. Defaults to 30 seconds.</summary>
    public TimeSpan KeepAlivePeriod { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the initial connection timeout. Defaults to 30 seconds.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the outbound and subscription QoS. Defaults to
    /// <see cref="MqttQualityOfService.AtLeastOnce"/>.
    /// </summary>
    public MqttQualityOfService QualityOfService { get; set; } = MqttQualityOfService.AtLeastOnce;

    /// <summary>
    /// Gets or sets whether outbound messages are retained by the broker. Defaults to
    /// <see langword="false"/>. Retained delivery happens when a client subscribes.
    /// </summary>
    public bool RetainMessages { get; set; }

    /// <summary>Gets or sets the first reconnect delay. Defaults to one second.</summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets the reconnect backoff ceiling. Defaults to 30 seconds.</summary>
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets an optional callback that customizes the MQTTnet client options (certificate
    /// validation, WebSocket transport, protocol version, and so on).
    /// </summary>
    public Action<MqttClientOptionsBuilder>? ConfigureClient { get; set; }

    /// <summary>Gets the message types this transport consumes.</summary>
    public IList<MqttSubscription> Subscriptions { get; } = [];
}

/// <summary>A message type this transport subscribes to.</summary>
public sealed class MqttSubscription {
    /// <summary>Gets or sets the message type to consume.</summary>
    public Type MessageType { get; set; } = typeof(object);

    /// <summary>
    /// Gets or sets an explicit topic filter. When omitted, the topic convention subscribes to the
    /// type's exact topic and its one-level tenant wildcard.
    /// </summary>
    public string? TopicFilter { get; set; }
}
