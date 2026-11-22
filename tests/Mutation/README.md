# Mutation testing configuration

Stryker.NET 5.1.0 runs from each test project directory, against the net11 asset. The nightly
CI job uses these five configs:

| Config | Project under test | Test project | Mutated surface |
|---|---|---|---|
| `tests/Jeninnet.CQRS.Tests/stryker-config.core.json` | `Jeninnet.CQRS` | `Jeninnet.CQRS.Tests` | dispatch (`CommandDispatcher`, `QueryDispatcher`, `CqrsBroker`, `DispatcherBase`) |
| `.../stryker-config.resilience.json` | `Jeninnet.CQRS.Resilience` | `Jeninnet.CQRS.Tests` | rate limit and partitioned concurrency behaviors |
| `.../stryker-config.idempotency.json` | `Jeninnet.CQRS.Idempotency` | `Jeninnet.CQRS.Tests` | idempotency behavior and in-memory store |
| `.../stryker-config.testing.json` | `Jeninnet.CQRS.Testing` | `Jeninnet.CQRS.Tests` | recording broker and assertions |
| `tests/Jeninnet.CQRS.Sagas.Tests/stryker-config.sagas.json` | `Jeninnet.CQRS.Sagas` | `Jeninnet.CQRS.Sagas.Tests` | saga dispatcher (correlation, transitions, follow-ups) |

## Exclusions

- **`ConfigureAwait`** — context capture is an asynchronous scheduling concern, not observable
  behavior under the test runner; mutating it produces equivalent mutants.
- **`Guard.*`** — argument-null/whitespace guards are defensive call-site checks. Their
  `Statement` mutants are equivalent for callers that satisfy the documented contract, and
  exhaustive null tests at every call site would add noise rather than behavior coverage.
- **Generated code** — `obj/`, `*.g.cs`, and source-generator output are never part of the
  explicit `mutate` lists.
- **Analyzer diagnostics** — `Jeninnet.CQRS.Analyzers` is a build-time package and is not in a
  mutation run.

Any other surviving mutant is test work, not an exclusion.

## Measured baseline and floor (2026-11-18, Stryker 5.1.0)

| Config | Killed | Survived | NoCoverage | Timeout | Score | Floor |
|---|---:|---:|---:|---:|---:|---:|
| core | 41 | 0 | 0 | 0 | 100.00% | 100 |
| resilience | 12 | 0 | 0 | 0 | 100.00% | 100 |
| idempotency | 56 | 2 | 1 | 2 | 95.08% | 95 |
| testing | 24 | 0 | 0 | 0 | 100.00% | 100 |
| sagas | 34 | 28 | 24 | 1 | 40.23% | 35 |

The score follows Stryker's definition: `(Killed + Timeout) / (Killed + Timeout + Survived +
NoCoverage)`. A timeout is a detected mutant because the test hangs on it. One saga dispatcher
statement mutant toggled between `Survived` and `Timeout` across runs, so the measured saga score
ranges 39.08–40.23; the recorded 40.23 is the unified nightly-run result and both values are above
the 35 floor.

## Initial surface and triage

The first gate intentionally starts with the highest-value public behavior files, not every
source file, so the nightly run stays inside its budget:

- **Core:** the four dispatch files. The 100% score is the ratchet target.
- **Resilience:** `RateLimitBehavior` and `PartitionedConcurrencyBehavior`. The retry/circuit
  breaker state machines and registries are the next test wave; their characterization tests
  exist but are not yet part of the gated surface.
- **Idempotency:** behavior + default store. The five non-killed mutants are recorded, not
  silently ignored: line 37 (options null-coalescing) and line 52 (`CancelAfter` statement) are
  timeouts where Stryker's host hangs rather than reporting an assertion failure; line 86 is an
  unreachable "unknown lease state" message; lines 126/128 are the best-effort heartbeat
  `Cancel()` and the awaiting of the heartbeat task in `finally`, whose cancellation also happens
  when the `using` scope disposes. These paths are the next store/heartbeat test wave.
- **Testing:** recorder + assertions, with string and null-coalescing message mutations excluded
  because message wording is not behavior. All positive/negative recorder and assertion paths are
  covered.
- **Sagas:** `SagaDispatcher` only. This is the weakest surface: compensation, retry, metrics,
  and several error branches still have survivors/no-coverage. The floor is intentionally low
  (35) and is the explicit backlog for the next saga test wave; the store/state-machine files are
  not in the first gated surface.

`scripts/mutation/run-all.ps1` runs all five configs, then
`scripts/mutation/summarize.ps1` writes `artifacts/mutation/dashboard.md` and `.json` with
survivors grouped by mutator. The nightly CI job uploads the whole `artifacts/mutation` folder.
