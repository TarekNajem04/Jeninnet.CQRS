# Packages

**Current preview:** `1.0.0-preview.1` — 31 packages.

Every package is documented from its XML comments under [API reference](xref:Jeninnet.CQRS). Package READMEs
remain the single source of truth for installation-specific notes and live in the repository.

Portable packages ship `netstandard2.0`, `net10.0`, and `net11.0` assets; the dispatch and
observability packages also ship `net462`. Validation, the result adapters, FluentValidation,
EF Core, ASP.NET Core, and SignalR are framework-blocked; see the [ADR registry](adr/README.md) for
the dependency, the net462 boundary, and the revisit trigger.

| Package | Purpose |
|---|---|
| [Jeninnet.CQRS](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS) | Broker, dispatchers, pipeline, cascading messages, deferred delivery, dead letters, outbox, request/response, persistent scheduler, multi-tenancy |
| [Jeninnet.CQRS.Contracts](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.Contracts) | Zero-dependency message markers, `[MessageVersion]`, and shared header constants |
| [Jeninnet.CQRS.Generators](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.Generators) | Source-generated handler registration |
| [Jeninnet.CQRS.Analyzers](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.Analyzers) | Compile-time misuse analyzers |
| [Jeninnet.CQRS.Validation](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.Validation) | Validation pipeline and result-adapter infrastructure |
| [Jeninnet.CQRS.FluentValidation](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.FluentValidation) | FluentValidation adapter |
| [Jeninnet.CQRS.Results.ErrorOr](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.Results.ErrorOr) | ErrorOr result adapter |
| [Jeninnet.CQRS.Results.FluentResults](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.Results.FluentResults) | FluentResults result adapter |
| [Jeninnet.CQRS.Results.CSharpFunctionalExtensions](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.Results.CSharpFunctionalExtensions) | CSharpFunctionalExtensions result adapter |
| [Jeninnet.CQRS.Caching](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.Caching) | Memory and distributed caching behaviors |
| [Jeninnet.CQRS.Resilience](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.Resilience) | Retry, circuit breaker, rate limiting, partitioned concurrency |
| [Jeninnet.CQRS.Idempotency](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.Idempotency) | Idempotency store abstraction and behavior |
| [Jeninnet.CQRS.Idempotency.EntityFrameworkCore](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.Idempotency.EntityFrameworkCore) | Durable EF Core stores (idempotency, deferred, dead letters, outbox, request inbox, scheduler, jobs) with tenant query filters, retention archives, and claim-check references |
| [Jeninnet.CQRS.ClaimCheck.FileSystem](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.ClaimCheck.FileSystem) | Content-addressed claim-check payloads on a durable local volume, with TTL cleanup |
| [Jeninnet.CQRS.ClaimCheck.AzureBlob](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.ClaimCheck.AzureBlob) | Azure Blob Storage claim-check provider with TTL cleanup |
| [Jeninnet.CQRS.ClaimCheck.AmazonS3](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.ClaimCheck.AmazonS3) | Amazon S3 claim-check provider with TTL cleanup |
| [Jeninnet.CQRS.Diagnostics](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.Diagnostics) | OpenTelemetry tracing, metrics, durability gauges |
| [Jeninnet.CQRS.HealthChecks](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.HealthChecks) | Broker, breaker, backlog, outbox, and stuck-saga health checks |
| [Jeninnet.CQRS.Testing](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.Testing) | Test broker, spies, and assertions |
| [Jeninnet.CQRS.AspNetCore](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.AspNetCore) | Minimal-API endpoint mapping |
| [Jeninnet.CQRS.Transport.RabbitMq](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.Transport.RabbitMq) | RabbitMQ transport with topology, confirms, dead-lettering, and reconnect |
| [Jeninnet.CQRS.Transport.Kafka](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.Transport.Kafka) | Kafka transport with topic topology, keyed partitions, groups, and manual commits |
| [Jeninnet.CQRS.Transport.AzureServiceBus](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.Transport.AzureServiceBus) | Azure Service Bus queues/topics, sessions, scheduling, and dead-letter forwarding |
| [Jeninnet.CQRS.Transport.Aws](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.Transport.Aws) | SQS/SNS transport with FIFO, visibility renewal, and claim-check handoff |
| [Jeninnet.CQRS.Transport.Mqtt](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.Transport.Mqtt) | MQTT topics with tenant levels, QoS 0/1/2, retained messages, and reconnect |
| [Jeninnet.CQRS.Transport.Http](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.Transport.Http) | Broker-free outbound HTTP POST transport with envelope headers, API-key/HMAC auth, and outbox-owned retry |
| [Jeninnet.CQRS.Transport.SignalR](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.Transport.SignalR) | Hub relay and reconnecting client transport for server↔client fan-out (`net10.0`/`net11.0`) |
| [Jeninnet.CQRS.Serialization.NewtonsoftJson](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.Serialization.NewtonsoftJson) | Newtonsoft.Json wire format negotiated by content type |
| [Jeninnet.CQRS.Serialization.MessagePack](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.Serialization.MessagePack) | MessagePack binary wire format with AOT-safe formatters |
| [Jeninnet.CQRS.Sagas](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.Sagas) | Explicit saga state machines, correlation, and in-memory persistence |
| [Jeninnet.CQRS.Sagas.EntityFrameworkCore](https://github.com/TarekNajem04/Jeninnet.CQRS/tree/main/src/Jeninnet.CQRS.Sagas.EntityFrameworkCore) | Durable saga state with optimistic concurrency and terminal-saga archives |