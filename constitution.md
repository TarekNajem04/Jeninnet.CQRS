# Jeninnet.CQRS Solution Constitution — v2.0 (Parity Mandate)

> **Amendment 2026-11-17 (v2.0).** The v1 constitution assumed an in-process-only
> mediator and treated broker transports, sagas, and legacy target frameworks as
> non-goals. That assumption is revoked. The project now competes with **every**
> CQRS/messaging library in the peer set on **every** capability. A capability is
> either shipped, or tracked by a task with a dated decision — never quietly declared
> out of scope.

## 1. Mission

Be the complete, permissively licensed CQRS and messaging platform for .NET:
in-process dispatch, durable delivery, broker transports, sagas, observability,
tooling — with no license key, no vendor lock-in, and no feature left "for later"
without an owner.

**Definition of done for the platform:** every row in the maintained parity matrix is `●` for
Jeninnet.CQRS, or carries a recorded ADR explaining why parity is impossible (vendor licensing of a peer's *own*
product is not a reason; our features must not require a paid dependency).

## 2. Technology stack

### Language / Runtime
- ➡️ **.NET 10 + .NET 11** for all runtime packages (`net10.0;net11.0`).
- ➡️ **`netstandard2.0`** is a **required** target for every package whose
  dependencies allow it, and **`net462`** is required for the core dispatch
  packages. Polyfills are allowed and expected (`IsExternalInit`, `Lock` shim,
  `TimeProvider` shim, `PriorityQueue` backport) when a BCL type is missing.
- ➡️ **C# 15** features wherever they improve clarity, safety, or performance:
  file-scoped namespaces, primary constructors, records, collection expressions
  (including `[with(StringComparer.Ordinal)]`), pattern matching, raw strings.
- ➡️ High-performance mindset (Span<T>, `ArrayPool`, no avoidable allocations),
  with **measured** claims (benchmarks are part of the gate).
- ➡️ Native AOT and trimming are **product requirements**, including the durable
  and transport paths — reflection must be generated away or annotated with a
  written justification.
- ➡️ Cross-platform: Windows, Linux, macOS; no platform-specific APIs in core.
- ➡️ Deep XML documentation on every public member; the docs site is generated
  from it.

### Dependencies
- ➡️ Permissive licenses only (MIT/Apache-2.0/BSD). A dependency that requires a
  paid runtime key is disqualifying.
- ➡️ Every transport/provider dependency lives in its **own package** so adopters
  pay only for what they use.
- ➡️ Central versioning in `Directory.Packages.props`; no floating versions.

### Project structure & code style
- ➡️ Centralized `GlobalUsings.cs` per project; no local `using` directives.
- ➡️ One package per capability (`Jeninnet.CQRS.Transport.RabbitMq`, `.Sagas`,
  `.Scheduling`, ...). The core stays free of transport types.
- ➡️ Solution folders mirror the architecture: `src/Core`, `src/Behaviors`,
  `src/Durability`, `src/Transports`, `src/Sagas`, `src/Tooling`, `src/Observability`.

## 3. Engineering standards

- ➡️ Martin Fowler: small steps, characterization tests first, refactorings that
  keep the build green; strangler pattern for replacing existing behavior.
- ➡️ Uncle Bob: TDD (red → green → refactor), Boy Scout Rule, clean code, single
  responsibility, dependency inversion; names mirror the domain, never invent
  synonyms for well-known concepts (`Outbox`, `Saga`, `ClaimCheck`).
- ➡️ Bertrand Meyer: contracts are documented (preconditions, invariants) and
  enforced at the boundary.
- ➡️ Respect `.editorconfig` and `.gitattributes` in every change.

## 4. Quality gates (all binding, all automated)

1. **Build**: 0 errors, 0 warnings across every target framework.
2. **Tests**: full suite green on `net10.0`, `net11.0`, `netstandard2.0`
   (compile + contract tests) and `net462` where applicable.
3. **Analyzers**: the analyzer message inventory must be empty before a task
   changes to `DONE`; IDE/RCS/CA/MSTEST messages are fixed at the source.
4. **Format**: `dotnet format --verify-no-changes` clean for every touched file.
5. **Docs**: public XML complete; package README updated; sample or snippet for
   every new capability.
6. **Performance**: benchmark baseline recorded for dispatch, durability, and each
   transport; a ≥20% regression fails CI.
7. **AOT**: trimmed publish of `AotSmoke` stays warning-free; new reflection is
   annotated or generated away.
8. **Coverage**: line coverage floor enforced in CI, ratchet-only.

A task may only move to `DONE` when gates 1–8 pass for its touched surface.

## 5. Governance

### Task lifecycle
- Every gap becomes a task in the maintainer's delivery ledger with group, status
  (`TODO` → `IN-PROGRESS` → `DONE`), matrix rows, and the compliance gate list.
- Closed tasks are immutable; follow-ups get new entries.
- The ledger index is the single source of status; it is updated in the same change
  as the status transition.

### Amendments
- Amendments require a written proposal in [`docs/adr/`](docs/adr/) and a version bump here.
- **ADR rule (new):** any capability we do *not* ship needs an ADR with a dated
  revisit trigger. "Non-goal" alone is no longer a valid disposition.

### Breaking changes
- Breaking changes are allowed while the version is `0.x`/`-preview`; they must be
  listed in `CHANGELOG.md` with a migration note.
- After `1.0`, SemVer applies to the public surface of every package.