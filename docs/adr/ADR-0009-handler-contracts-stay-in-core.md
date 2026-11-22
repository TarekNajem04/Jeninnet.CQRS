# ADR-0009 — Handler and dispatcher interfaces stay in core

**Status:** Accepted · **Date:** 2026-11-19

## Context

`CQRS-65` introduces `Jeninnet.CQRS.Contracts`, a zero-dependency package for message shapes and
metadata shared by producers and consumers. The package intentionally contains only passive
contracts: `ICommand`, `IQuery`, `INotification`, `IEvent`, `IStreamQuery`, `Unit`,
`[MessageVersion]`, and header constants.

Handler contracts (`ICommandHandler`, `IQueryHandler`, `INotificationHandler`), pipeline
behaviors, dispatchers, and processors describe runtime behavior and depend on the DI-oriented
execution model. Moving them would force every contract-only producer to carry interfaces it
cannot sensibly implement.

## Decision

Keep handler and dispatcher interfaces in `Jeninnet.CQRS`. Do not add them to
`Jeninnet.CQRS.Contracts`, and do not type-forward them until a concrete standalone-consumer
scenario requires it.

## Consequences

- The contracts package stays framework-only and safe for another repository to reference.
- Message producers still get every marker type and version/header metadata they need.
- Handler implementations continue to reference the runtime package, as before.

## Revisit trigger

Revisit if a consumer-only deployment needs to compile handler-shaped adapters without the core
runtime, or if a peer-compatible handler contract can be expressed without DI/behavior types.
Target review date: **2027-03-31**.
