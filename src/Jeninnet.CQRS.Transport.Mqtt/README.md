# Jeninnet.CQRS.Transport.Mqtt

MQTT transport for `Jeninnet.CQRS`: topic-per-message-type routing with an optional tenant level,
QoS 0/1/2, retained-message support, clean-session control, and an owned reconnect loop.

```csharp
builder.Services.AddCqrsMessageSerialization();
builder.Services.AddCqrsMqtt(options => {
    options.HostName = "broker.example.com";
    options.Port = 8883;
    options.UseTls = true;
    options.UserName = configuration["Mqtt:User"];
    options.Password = configuration["Mqtt:Password"];
    options.QualityOfService = MqttQualityOfService.AtLeastOnce;
    options.Subscriptions.Add(new MqttSubscription { MessageType = typeof(OrderCreated) });
});

await transport.PublishAsync(new OrderCreated(id), new TransportMessageOptions {
    TenantId = tenantId,
    CorrelationId = correlationId,
});
```

`AddCqrsMqtt` selects `CqrsTransportMode.External`, registers the transport, and adds a hosted
service that starts/stops the client with the host. User registrations made before it win
(`TryAdd`).

## Topics

The single `MqttTopicConvention` formatter owns every topic:

- `cqrs/{message-type}` for tenant-less messages.
- `cqrs/{message-type}/{tenant}` when `TenantId` is set.
- Topic levels are sanitized: `/`, `+`, `#`, whitespace, and other unsafe characters become `-`,
  so caller data cannot create wildcards or extra levels.
- A subscription listens on the exact topic and `{topic}/+`, covering tenant-less and
  tenant-specific messages. An explicit `MqttSubscription.TopicFilter` replaces this pair.
- The prefix is configurable through `new MqttTopicConvention("edge/cqrs")`; the package registers
  `cqrs` by default.

The topic is the routing key; the assembly-qualified message type, schema version, content type,
correlation/causation, partition key, delivery mode, timestamp, tenant, and custom headers travel
as MQTT 5 user properties. A message without user properties still resolves the type from the
subscription topic, but the full metadata contract requires MQTT 5; MQTT 3.1.1 metadata behavior
is out of scope.

## Quality of service

MQTT QoS is a guarantee between one client and the broker, not an end-to-end handler guarantee:

| Setting | Broker behavior | Handler caveat |
|---|---|---|
| `AtMostOnce` (0) | Fire and forget | A handler failure or disconnect loses the message |
| `AtLeastOnce` (1) | PUBACK after receipt; duplicates possible | Handler effects must be idempotent |
| `ExactlyOnce` (2) | Two-phase PUBREC/PUBREL/PUBCOMP flow | A crash after handling but before acknowledgement can still redeliver |

The transport acknowledges only after the handler (or the full local handler fan-out) succeeds and
sets `ProcessingFailed` on failure. That gives at-least-once dispatch in every QoS mode; use the
consumer inbox for de-duplication. QoS 2 is **not** a transactional exactly-once claim for user
code.

`Send` and `Publish` both travel on the same MQTT topic because MQTT has no broker-side
point-to-point queue. The delivery-mode user property tells this transport whether to dispatch to
the first local handler (`Send`) or every local handler (`Publish`). Other subscribers still
receive the topic message; use one topic per consumer where fan-out must be isolated.

## Retained messages and reconnection

`RetainMessages = true` asks the broker to retain the last message per topic; a late subscriber
receives it when it subscribes. Retained delivery is not a durable event log: a new message
replaces the retained one, and clearing it requires an empty retained publish (outside this
package).

The transport owns reconnection. An unexpected disconnect starts an exponential backoff
(`ReconnectDelay` up to `MaxReconnectDelay`), then reconnects and re-subscribes. Sends during a
reconnect throw so the outbox relay retries instead of silently dropping the message. A stable
`ClientId` is required when `CleanSession = false`; the default is a generated id per transport
with a clean session.

## Options

| Option | Default | Purpose |
|---|---|---|
| `HostName` | `localhost` | Broker host |
| `Port` | `1883` | Broker port |
| `ClientId` | generated | Stable id required for durable sessions |
| `UserName` / `Password` | `null` | Optional credentials |
| `UseTls` | `false` | TLS TCP connection |
| `CleanSession` | `true` | Broker session behavior |
| `KeepAlivePeriod` | 30 s | MQTT keep-alive |
| `ConnectTimeout` | 30 s | Initial connection budget |
| `QualityOfService` | `AtLeastOnce` | Publish and subscription QoS |
| `RetainMessages` | `false` | Retain outbound messages |
| `ReconnectDelay` | 1 s | First reconnect delay |
| `MaxReconnectDelay` | 30 s | Backoff ceiling |
| `Subscriptions` | empty | Message types (and optional filters) to consume |
| `ConfigureClient` | `null` | Customize `MqttClientOptionsBuilder` (certificates, WebSocket, and so on) |

The package targets `netstandard2.0`, `net10.0`, and `net11.0` on MQTTnet 4.3.7. MQTTnet 5 dropped
the `netstandard2.0` asset; `ADR-0011` records the dependency decision and revisit trigger.