# ADR-0002 — Result adapters stay off netstandard2.0

**Status:** Accepted (blocked transitively) · **Date:** 2026-11-18

## Context

The three result adapters (`Jeninnet.CQRS.Results.ErrorOr`,
`.Results.FluentResults`, `.Results.CSharpFunctionalExtensions`) all reference
`Jeninnet.CQRS.Validation`. ErrorOr 2.1.1 and FluentResults 4.0.0 ship `netstandard2.0` legs;
CSharpFunctionalExtensions 3.7.0 is portable as well. The only blocker is the project reference
to Validation, which is net10-only (ADR-0001).

## Decision

Keep all three adapters on `net10.0;net11.0` until Validation is portable. Do not duplicate the
validation mapping in the adapters. This also excludes `net462`; see ADR-0006.

## Consequences

- No `netstandard2.0` asset for any result adapter.
- The blocked surface is exactly three packages, all unblocked together by ADR-0001's trigger.

## Revisit trigger

Unblock when `Jeninnet.Validation.Abstractions` ships a `netstandard2.0` leg, then add the TFM to
Validation and the three adapters in one change. Target review date: **2026-12-31**.
