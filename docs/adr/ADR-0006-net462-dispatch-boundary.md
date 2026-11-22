# ADR-0006 — net462 reach is limited to dispatch and observability packages

**Status:** Accepted · **Date:** 2026-11-18

## Context

.NET Framework 4.6.2 hosts still exist in enterprise deployments. `CQRS-54` adds `net462` to
the packages that form the dispatch pipeline and can be built and exercised on Windows:

- `Jeninnet.CQRS`
- `Jeninnet.CQRS.Caching`
- `Jeninnet.CQRS.Idempotency`
- `Jeninnet.CQRS.Resilience`
- `Jeninnet.CQRS.Diagnostics`
- `Jeninnet.CQRS.HealthChecks`
- `Jeninnet.CQRS.Testing`
- `Jeninnet.CQRS.Sagas`

The Microsoft.Extensions 11 packages used by this set (DI, Options, Logging, Hosting
Abstractions, Caching.Memory, HealthChecks, Configuration.Binder) all ship `net462` assets, as do
System.Text.Json, System.Diagnostics.DiagnosticSource, System.Threading.RateLimiting, and
System.Runtime.Caching. The `CQRS-53` polyfill layer is reused for `TimeProvider`, `Lock`,
`PriorityQueue`, compiler attributes, and the guard/string/crypto helpers.

## Decision

`net462` is a **supported but frozen** surface for dispatch, caching, idempotency, resilience,
diagnostics, health checks, testing, and sagas. No API is added only for the legacy TFM; the
same handlers and behaviors compile on every target.

The following packages deliberately do not add `net462`:

- **EF Core stores** — EF Core 11 is net11-only (ADR-0004).
- **Validation and the three result adapters** — blocked by
  `Jeninnet.Validation.Abstractions` (ADR-0001/0002).
- **FluentValidation bridge** — FluentValidation 12 is net8-only (ADR-0003).
- **ASP.NET Core adapter** — no net462 shared framework (ADR-0005).
- **Transports and wire formats** — out of scope for this dispatch task. Their broker SDKs have
  mixed legacy floors (RabbitMQ.Client 7.2.2 has netstandard2.0 but no net462; AWS SDK 4 has
  net472/netstandard2.0 but no net462). A host can still consume their netstandard2.0 assets from
  net462 with NuGet's compatibility fallback, but we do not claim a supported net462 transport
  surface until each broker is characterized.

## Consequences

- `samples/Jeninnet.CQRS.Sample.NetFramework` is the characterization test: it builds and runs
  one command and one query on .NET Framework 4.6.2 and exits non-zero on a mismatch.
- Legacy hosts get the full in-process dispatch pipeline; durable EF stores and transport
  hosting remain modern-TFM concerns.
- The `net462` assets are included in the NuGet packages and verified by the package scan.

## Revisit trigger

Revisit transports/formats when a legacy transport host is a committed target and each broker
SDK's net462 fallback is characterized; revisit EF/validation when their upstream dependencies
ship compatible assets. Target review date: **2026-12-31**.
