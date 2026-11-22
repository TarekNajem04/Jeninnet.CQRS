# ADR-0004 — EF Core stores stay net11-only

**Status:** Accepted (vendor-blocked) · **Date:** 2026-11-18

## Context

`Jeninnet.CQRS.Idempotency.EntityFrameworkCore` and `Jeninnet.CQRS.Sagas.EntityFrameworkCore`
reference Microsoft.EntityFrameworkCore `11.0.0`, which ships a `net11.0` asset only. EF Core
relies on runtime features that are unavailable on `netstandard2.0`.

## Decision

Keep both EF Core packages on `net11.0`. Do not add a legacy EF Core major or an alternate
persistence stack inside these packages. This also excludes `net462`; see ADR-0006.

## Consequences

- Durable stores (idempotency, deferred, outbox, dead letters, request inbox, scheduler) and EF
  saga persistence are available on net11 only.
- The in-memory equivalents remain available on all supported TFMs, including netstandard2.0.
- Hosts needing durable stores on older frameworks can implement the port interfaces against
  their own database and register them through DI.

## Revisit trigger

Revisit if Microsoft.EntityFrameworkCore ships a `netstandard2.0` asset, or if a deliberate
EF Core 6/7 compatibility package is desired for legacy hosts. Target review date:
**2026-12-31**.
