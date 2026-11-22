# Jeninnet.CQRS.Transport.Http

Outbound HTTP transport for `Jeninnet.CQRS`: POST the canonical envelope to a remote endpoint with
API-key and/or HMAC-SHA256 authentication. The adapter has no broker, no consumer loop, and no
internal retry; the outbox relay remains the single owner of retry policy.

```csharp
builder.Services.AddCqrsMessageSerialization();
builder.Services.AddCqrsHttpTransport(options => {
    options.Endpoint = "https://orders.example.com/messages";
    options.ApiKey = configuration["Cqrs:Http:ApiKey"];
    options.HmacSecret = configuration["Cqrs:Http:HmacSecret"];
});

await transport.PublishAsync(new OrderCreated(id), new TransportMessageOptions {
    CorrelationId = correlationId,
    TenantId = tenantId,
});
```

`AddCqrsHttpTransport` selects `CqrsTransportMode.External`, registers the named HTTP client, and
adds a hosted service that starts/stops the transport with the host. User registrations made before
it win (`TryAdd`).

## Wire format

- The request body is the serialized payload (`Content-Type` from the configured serializer).
- `TransportHeaders` metadata (`message-type`, `message-version`, `content-type`, `x-tenant-id`)
  is sent as HTTP headers.
- Envelope fields use `HttpTransportHeaders` (`x-cqrs-message-id`, `x-cqrs-correlation-id`,
  `x-cqrs-causation-id`, `x-cqrs-partition-key`, `x-cqrs-delivery-mode`, `x-cqrs-timestamp`,
  topology names).
- Caller headers are prefixed with `x-cqrs-header-` and restored by
  `HttpTransportWire.ReadEnvelope(headers, body)`.

The receive side can call `HttpTransportWire.ReadEnvelope` and
`HttpTransportWire.VerifySignature` instead of reimplementing the header contract. Inbound
handler-to-HTTP mapping is the separate `CQRS-32` feature in `Jeninnet.CQRS.AspNetCore`.

## Authentication and failures

- `ApiKey` writes `x-api-key` (configurable through `ApiKeyHeader`).
- `HmacSecret` writes `sha256=<hex>` over the raw body (configurable through `HmacHeader`).
- 401 and 403 responses throw `HttpTransportException` with an explicit authentication message and
  the bounded response body; any other non-success status throws the same exception with the
  status and body. Network exceptions propagate unchanged.

No retry, backoff, or circuit breaker is implemented here. Run the transport behind the outbox
relay so a failed POST leaves the row pending and is retried with the relay's policy.

## Options

| Option | Default | Purpose |
|---|---|---|
| `Endpoint` | required | Absolute URL or a path relative to the HTTP client base address |
| `HttpClientName` | `Jeninnet.CQRS.Transport.Http` | Named `IHttpClientFactory` client |
| `ApiKey` | `null` | API key sent on every request |
| `ApiKeyHeader` | `x-api-key` | API-key header name |
| `HmacSecret` | `null` | HMAC-SHA256 secret for body signatures |
| `HmacHeader` | `x-cqrs-signature` | Signature header name |
| `Timeout` | 30 s | Per-request timeout |

The package targets `netstandard2.0`, `net10.0`, and `net11.0`.