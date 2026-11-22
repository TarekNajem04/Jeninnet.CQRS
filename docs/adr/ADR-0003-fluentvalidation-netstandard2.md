# ADR-0003 — FluentValidation bridge stays off netstandard2.0

**Status:** Accepted (vendor-blocked) · **Date:** 2026-11-18

## Context

`Jeninnet.CQRS.FluentValidation` references FluentValidation `12.2.0`, whose lowest NuGet asset
is `net8.0`. The bridge cannot compile for `netstandard2.0` without downgrading the validation
engine and losing the v12 API surface the package intentionally exposes.

## Decision

Keep `Jeninnet.CQRS.FluentValidation` on `net10.0;net11.0`. Do not add a second FluentValidation
major just for an old TFM. This also excludes `net462`; see ADR-0006.

## Consequences

- FluentValidation users on `netstandard2.0`/`net462` cannot use this bridge.
- They can still use the portable validation seam once ADR-0001 unblocks, or write a host-local
  adapter against their FluentValidation version.

## Revisit trigger

Revisit if FluentValidation ships a `netstandard2.0` asset, or if a conditional `FluentValidation
11.x` dependency can be added without changing the public bridge API. Target review date:
**2026-12-31**.
