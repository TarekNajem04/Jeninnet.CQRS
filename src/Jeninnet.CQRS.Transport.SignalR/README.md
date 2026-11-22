# Jeninnet.CQRS.Transport.SignalR

SignalR transport for `Jeninnet.CQRS`: a client-side `IMessageTransport` that consumes hub
deliveries with automatic reconnection, plus a minimal relay hub for server↔client messaging.

```csharp
// Server
builder.Services.AddSignalR();
app.MapCqrsSignalRHub("/cqrs");

// Client
builder.Services.AddCqrsMessageSerialization();
builder.Services.AddCqrsSignalR(options => {
    options.HubUrl = "https://orders.example.com/cqrs";
    options.ConfigureConnection = connection => {
        connection.AccessTokenProvider = () => tokenProvider.GetTokenAsync();
    };
});
```

`AddCqrsSignalR` selects `CqrsTransportMode.External`, registers the transport and a hosted
service, and wires the shared receive pipeline. User registrations made before it win (`TryAdd`).

## Delivery model

- **Publish** invokes the hub's `Publish` method; the relay hub fans the envelope out to every
  connected client, including the sender. Each client transport dispatches to all registered
  handlers.
- **Send** invokes the hub's `Send` method; the relay returns the envelope to the calling
  connection, whose transport dispatches to the first handler. SignalR has no broker-side
  point-to-point queue; use hub groups or a broker when another consumer must receive it.
- Inbound envelopes are rebuilt with `TransportEnvelope` and pass through the core receive
  pipeline (filters, middleware, retry, consumption timeout).

SignalR delivery is ephemeral and at-most-once across live connections: a disconnected client
misses messages, and there is no acknowledgement or redelivery. Pair the transport with the
outbox/relay for durable handoff where the source is a local transaction.

## Reconnection

`HubConnection` is owned by the transport. Unexpected disconnects use the automatic reconnect
policy from `ReconnectDelays` (immediate, then 2, 5, 10, and 30 seconds by default); the final
delay repeats. Sends and publishes during a reconnect throw `InvalidOperationException`, so the
relay can retry instead of silently dropping the message. `ConnectionState` exposes the current
`HubConnectionState` for diagnostics.

## Scale-out and backplane

The hub and transport are stateless; any server can accept a connection. For more than one server,
configure an ASP.NET Core SignalR backplane (for example Redis) so publishes reach clients connected
to every instance. The transport does not bundle a backplane.

## Options

| Option | Default | Purpose |
|---|---|---|
| `HubUrl` | `/cqrs` | Hub URL, absolute or relative to the client base address |
| `AutomaticReconnect` | `true` | Reconnect after an unexpected disconnect |
| `ReconnectDelays` | 0, 2, 5, 10, 30 s | Delay schedule; the final entry repeats |
| `StartTimeout` | 30 s | Initial connection budget |
| `ConfigureConnection` | `null` | Customize `HttpConnectionOptions` (tokens, handlers, transports) |

The package targets `net10.0` and `net11.0` because the server-side relay hub uses the ASP.NET Core
shared framework. The client package it depends on also supports `netstandard2.0`; a standalone
client-only package can be split later if a `netstandard2.0` producer needs it.