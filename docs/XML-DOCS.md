# XML documentation policy

The API site is generated from XML comments, so the shipped packages must keep those comments
complete. `CS1591` (missing XML comment for a publicly visible member) is **enabled** for every
packable project and enforced by the normal build gate (zero warnings).

| Scope | CS1591 | Rationale |
|---|---|---|
| `src/**` packable packages | enabled | The DocFX site is generated from their XML docs; a missing comment becomes a missing page. |
| `tests/**` | suppressed via `Directory.Build.targets` (`IsPackable=false`) | Test doubles and fixtures are not published. |
| `samples/**` | suppressed via `Directory.Build.targets` (`IsPackable=false`) | Demo code is not part of the package surface. |
| `tests/Benchmarks/**` | suppressed via `Directory.Build.targets` (`IsPackable=false`) | Benchmark harness code is not published. |

The suppression is a build-scope decision, not a per-file exemption: a new packable library gets
CS1591 automatically. Add XML comments instead of adding `<NoWarn>1591</NoWarn>` to a shipped
project. Generated code (source generators) is exempt from CS1591 by the compiler.

The current shipped-package budget is **zero warnings**. If a temporary exception is unavoidable,
scope it to the single project and record the reason in this file in the same PR.
