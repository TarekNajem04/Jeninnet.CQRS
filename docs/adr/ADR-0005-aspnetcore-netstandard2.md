# ADR-0005 — ASP.NET Core endpoint mapping stays on modern TFMs

**Status:** Accepted (framework-blocked) · **Date:** 2026-11-18

## Context

`Jeninnet.CQRS.AspNetCore` uses `<FrameworkReference Include="Microsoft.AspNetCore.App" />`.
ASP.NET Core has no `netstandard2.0` shared framework; endpoint routing and minimal APIs require
a modern ASP.NET Core TFM.

## Decision

Keep the endpoint package on `net10.0;net11.0`. Hosts on older TFMs can call the broker directly
or map their own endpoints; the dispatch pipeline itself is portable. This also excludes
`net462`; see ADR-0006.

## Consequences

- No `netstandard2.0` asset for the ASP.NET Core adapter.
- This is a framework boundary, not a vendor package gap.

## Revisit trigger

Only if a portable endpoint abstraction independent of the ASP.NET Core shared framework is
designed. No date-based revisit is planned.
