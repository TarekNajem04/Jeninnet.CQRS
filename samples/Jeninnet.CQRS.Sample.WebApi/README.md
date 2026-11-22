# Jeninnet.CQRS.Sample.WebApi

WebApi demo: the CQRS pipeline (`AddCqrsByReflection` plus validation, logging,
metrics, error handling) behind controllers, with health checks and OpenAPI in
development. Durable idempotency is wired to SQLite (`idempotency.db`).

## Run

```powershell
dotnet run --project samples/Jeninnet.CQRS.Sample.WebApi
```

Then open `https://localhost:7003/openapi/v1.json` (development) for the
OpenAPI document, or check `https://localhost:7003/healthz` for the
health-check entries (`http://localhost:5041` serves plain HTTP).

The sample registers one command (`CreateSampleCommand`) and one query
(`GetSampleQuery`); the weather-forecast controller is the untouched webapi
template.

## Broker-free transports

`CQRS-66` wires both lightweight transports without changing the default
behavior. Set `Cqrs:Transport` to `Http` or `SignalR` in `appsettings.json`
(or via environment variables) to exercise one:

- `Http` registers `AddCqrsHttpTransport` with the configured endpoint, API key,
  and HMAC secret. Point `Cqrs:Http:Endpoint` at a receiver that uses
  `HttpTransportWire.ReadEnvelope`.
- `SignalR` registers the client transport against the hub mapped at `/cqrs`.
  The relay hub is always mapped, so the sample can also act as the server for a
  separate client process.

`None` (the default) keeps the in-process pipeline.