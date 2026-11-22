//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Mqtt.Tests;

internal static class MqttTestOptions {
    internal static MqttTransportOptions Create(string host, int port, string prefix) {
        var options = new MqttTransportOptions {
            HostName = host,
            Port = port,
            ClientId = $"{prefix}-client",
            CleanSession = true,
            KeepAlivePeriod = TimeSpan.FromSeconds(5),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ReconnectDelay = TimeSpan.FromMilliseconds(100),
            MaxReconnectDelay = TimeSpan.FromMilliseconds(500),
            QualityOfService = MqttQualityOfService.AtLeastOnce,
        };

        options.Subscriptions.Add(new MqttSubscription { MessageType = typeof(TransportContractMessage) });

        return options;
    }
}