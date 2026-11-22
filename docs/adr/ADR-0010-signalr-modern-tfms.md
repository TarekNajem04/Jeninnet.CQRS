# ADR-0010 — SignalR transport stays on modern TFMs

**Status:** Accepted · **Date:** 2026-11-19

## Context

`CQRS-66` adds `Jeninnet.CQRS.Transport.SignalR` with two halves:

- `SignalRTransport`, a client-side `IMessageTransport` built on
  `Microsoft.AspNetCore.SignalR.Client`; the client package itself supports `netstandard2.0`.
- `CqrsTransportHub`, a relay hub built on the ASP.NET Core shared framework.

A `netstandard2.0` asset cannot reference `Microsoft.AspNetCore.App` (`NETSDK1073`), and the
client's `Microsoft.Bcl.TimeProvider` dependency conflicts with the core's public `TimeProvider`
polyfill on that target. Shipping only the client on `netstandard2.0` would split the package's
public API by target framework and leave the hub unavailable from the same assembly.

## Decision

Target `net10.0` and `net11.0` for the SignalR package. Keep the relay hub and the client
transport in one assembly so the sample, tests, and documentation describe one contract.

## Consequences

- Hosts must run on .NET 10 or later to use the package.
- The HTTP transport remains `netstandard2.0`/`net10.0`/`net11.0`; broker-free producers that need
  legacy targets can use HTTP or the contracts package.
- The client-only `netstandard2.0` split remains deliberately deferred rather than partially
  shipping an API surface that cannot include the hub.

## Revisit trigger

Revisit if a producer on `netstandard2.0`/`net462` needs to consume SignalR, or when the
`Microsoft.Bcl.TimeProvider` conflict is resolved by aligning core on the BCL package. Target
review date: **2027-03-31**.
