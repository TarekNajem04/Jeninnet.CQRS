# Roadmap

Direction for Jeninnet.CQRS. Items are ordered by release, not by promised date.
The delivery ledger is maintained in the maintainer's local workspace; this roadmap is the public
summary.

## Release principles

- **SemVer.** Previews use `1.0.0-preview.N`, release candidates use `1.0.0-rc.N`, stable uses
  `1.0.0`; additive features use minor versions; breaking changes require a major version.
- **One product version.** `Directory.Build.props` owns the single solution-wide `<Version>`;
  package projects must not declare their own version. `AssemblyVersion`/`FileVersion` stay
  numeric because they cannot carry a prerelease label.
- **Release gates.** The full build, test, format, analyzer, DocFX, AOT, pack, and benchmark
  gates must pass before a preview is tagged.
- **Previews are explicit.** Pin `1.0.0-preview.N`; preview and RC counts are not fixed and can be
  extended when feedback or release-gate evidence requires another pass.

## Status at a glance

| Release line | Status | Scope |
|---|---|---|
| `1.0.0-preview.1` | **Current — prepared 2026-11-22** | First public preview: full feature set, 31 packages |
| `1.0.0-preview.2` … `preview.N` | **Conditional** | Post-preview fixes and API feedback; no new scope |
| `1.0.0-rc.1` … `rc.N` | **Conditional** | Release-candidate hardening; release-blocking fixes only |
| `1.0.0` | **Planned** | Public-surface stabilization; compiled EF Core models; schema registry |
| `1.1.0-preview.1` … `1.1.0` | **Planned** | Additive post-1.0 capabilities driven by adopter feedback |

# 1.0.0-preview.1 — Current, prepared 2026-11-22

**Goal:** the first public preview of the complete feature set. It ships in-process dispatch and
durability, validation/result adapters, sagas and scheduling,
RabbitMQ/Kafka/Azure Service Bus/AWS/MQTT/HTTP/SignalR transports, contracts, observability,
docs, AOT, and 31 packages. Release gates: 0/0 Debug/Release builds, 28/28 test-assembly runs,
DocFX 0/0, AOT smoke, 31 packages/29 symbols, and the dispatch benchmark fast set.

**Remaining after this preview:** collect API feedback; freeze the public surface for `1.0.0`;
ship compiled EF Core models and schema-registry integration; revisit the deferred ADRs
(`ADR-0007` GCS claim-check, `ADR-0008` cold-storage export, `ADR-0010` SignalR
`netstandard2.0` client split, `ADR-0011` MQTTnet 5); run the quarterly release audit.

## Completed

- [x] Core abstractions: commands, queries, handlers, dispatcher, broker, pipeline behaviors
- [x] Source-generated handler registration (`Jeninnet.CQRS.Generators`)
- [x] Pipeline behaviors: caching, idempotency, resilience (retry/circuit-breaker), logging, error handling
- [x] Validation behavior backed by the standalone `Jeninnet.Validation` engine (NuGet)
- [x] FluentValidation bridge
- [x] `.NET 10` + `.NET 11` / C# 15 baseline
- [x] Test suite on `Jeninnet.Assertions` with the MSTest adapter
- [x] Shared platform repository layout (`.github`, `scripts`, `.config`, solution folders)
- [x] Notification fan-out, streaming queries, transaction behavior, diagnostics, health checks, rate limiting, handler test-kit, misuse analyzers
- [x] In-process scheduled delivery, one-generic void-handler dispatch, durable EF Core idempotency store
- [x] Result-type pipeline (`ErrorOr`, `FluentResults` adapters, opt-in)
- [x] NativeAOT trim smoke for the generated path with a CI publish job
- [x] Samples (Console, WebApi, EndToEnd, AotSmoke) with run instructions; root getting-started README
- [x] Durable deferred delivery with startup recovery and a relay seam

## Wave 1 — in-process foundation

- [x] Convention-based handler discovery (`CQRS-21`)
- [x] Cascading messages (`CQRS-22`)
- [x] Typed exception handlers and actions (`CQRS-23`)
- [x] Startup DI validation (`CQRS-24`)
- [x] Keyed concurrency partitioning (`CQRS-25`)
- [x] `CSharpFunctionalExtensions` result adapter (`CQRS-30`)
- [x] Batch dispatch and message versioning (`CQRS-36`)
- [x] Graceful shutdown draining (`CQRS-37`)

## Wave 2 — durable core and tooling

- [x] Dead-letter store and replay (`CQRS-26`)
- [x] Durable notification outbox (`CQRS-27`)
- [x] Pluggable serializer and claim-check (`CQRS-28`)
- [x] Distributed cache behavior (`CQRS-29`)
- [x] Durability observability (`CQRS-31`)
- [x] Benchmarks, API docs site, publish + coverage gates (`CQRS-33`, `CQRS-34`, `CQRS-35`)

## Wave 3 — transports

- [x] Transport abstractions + in-memory transport (`CQRS-41`)
- [x] RabbitMQ (`CQRS-42`), Kafka (`CQRS-43`), Azure Service Bus (`CQRS-44`), AWS SQS/SNS (`CQRS-45`)
- [x] Serialization formats (`CQRS-46`), outbox relay workers (`CQRS-47`)
- [x] Request/response over a transport (`CQRS-50`)

## Wave 4 — sagas and scheduling

- [x] Saga state machine + EF persistence (`CQRS-48`)
- [x] Saga timeouts, compensation, observability (`CQRS-49`)
- [x] Persistent message scheduler (`CQRS-51`)

## Wave 5 — reach and quality

- [x] Multi-tenancy (`CQRS-52`)
- [x] `netstandard2.0` for every package that can (`CQRS-53`)
- [x] `net462` for the dispatch packages (`CQRS-54`)
- [x] Mutation testing gate (`CQRS-55`)

## Wave 6 — messaging completeness

- [x] Consumer endpoints, filters, middleware, and inbox (`CQRS-56`, `CQRS-57`)
- [x] Routing slips, distributed jobs, observers, saga harness, claim-check providers (`CQRS-58`–`CQRS-62`)
- [x] Multi-transport riders and retention archives (`CQRS-63`, `CQRS-64`)
- [x] Zero-dependency message contracts (`CQRS-65`)
- [x] HTTP, SignalR, and MQTT transports (`CQRS-66`, `CQRS-67`)
- [x] Release audit, ADR registry, and quarterly process (`CQRS-68`)

## Post-preview / 1.0.0 candidates

- [ ] Stabilize and freeze the public surface toward `1.0.0`
- [ ] Compiled EF Core models for the durable stores (trimming)
- [ ] Schema registry integration for contract-first messaging