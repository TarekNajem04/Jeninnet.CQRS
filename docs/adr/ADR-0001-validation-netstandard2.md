# ADR-0001 — Validation package stays off netstandard2.0

**Status:** Accepted (blocked) · **Date:** 2026-11-18

## Context

`Jeninnet.CQRS.Validation` references `Jeninnet.Validation.Abstractions`
`1.0.0-preview.2`, whose NuGet asset is `net10.0` only. The validation behavior and its
`ISyncValidator` adapters cannot compile for `netstandard2.0` without that assembly.

## Decision

Keep `Jeninnet.CQRS.Validation` on `net10.0;net11.0`. Do not fork or reimplement the sibling
validation contract in this repository; the abstraction is owned by the sibling package. This
also excludes `net462`; see ADR-0006.

## Consequences

- `Jeninnet.CQRS.Validation` has no `netstandard2.0` asset.
- The result adapters inherit the same block (see ADR-0002).
- The rest of the pipeline (dispatch, caching, idempotency, resilience, diagnostics,
  health checks, testing, sagas, transports, serialization) ships `netstandard2.0` without it.

## Revisit trigger

Revisit when `Jeninnet.Validation.Abstractions` publishes a `netstandard2.0` leg, or when the
sibling repository exposes a portable validation contract we can reference without net10 APIs.
Target review date: **2026-12-31**.
