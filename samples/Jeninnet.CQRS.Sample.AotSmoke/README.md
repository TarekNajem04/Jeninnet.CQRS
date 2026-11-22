# Jeninnet.CQRS.Sample.AotSmoke

NativeAOT smoke for CQRS-10 and CQRS-39, dispatched and rehydrated through the
**generated** path (`AddCqrs()`), proving the claim "no runtime reflection on the
dispatch or durable-rehydration path" with a published binary instead of
marketing.

## Run locally (trim analysis, no native toolchain needed)

```powershell
# Dispatch proof (CQRS-10)
dotnet publish samples/Jeninnet.CQRS.Sample.AotSmoke -f net11.0 -p:PublishAot=false
dotnet samples/Jeninnet.CQRS.Sample.AotSmoke/bin/Release/net11.0/win-x64/publish/Jeninnet.CQRS.Sample.AotSmoke.dll
# Hello, AOT!
# Quote #7

# Durable proof (CQRS-39): two processes, one file-backed deferred store.
dotnet samples/Jeninnet.CQRS.Sample.AotSmoke/bin/Release/net11.0/win-x64/publish/Jeninnet.CQRS.Sample.AotSmoke.dll stage
dotnet samples/Jeninnet.CQRS.Sample.AotSmoke/bin/Release/net11.0/win-x64/publish/Jeninnet.CQRS.Sample.AotSmoke.dll replay
# Staged 2 deferred commands.
# greet:AOT, result:42
```

`stage` exits before the pump starts. `replay` is a fresh process that recovers
the JSON-line store through the generated `IDeferredEnvelopeFactory`: one void
`ICommand` and one `ICommand<T>` command, covering both envelope shapes. The
sample's `AotJsonMessageSerializer` uses a `JsonSerializerContext`, so payload
serialization is source-generated too.

`TreatWarningsAsErrors` promotes every `IL2xxx` trim/AOT warning to an error, so
a clean publish is the proof. Full AOT compilation needs a native toolchain
(MSVC on Windows, clang on Linux) and is proven by the `aot-smoke` CI job, which
publishes `-r linux-x64`, runs the dispatch proof, then stages and replays across
two processes (exit code 0 expected).

## What stays reflection-based (and how to avoid it)

- `AddCqrsByReflection` scans assemblies at runtime, including its custom
  `IPipelineBehavior` discovery. Trimming may remove handlers the scan would
  have found. For AOT, register with the generated `AddCqrs()` instead.
- The generated registry covers commands declared in the compiling assembly.
  Commands from a referenced contracts assembly are not covered and take the
  annotated reflection fallback in the store; declare them in the application
  assembly for a fully trimmed deployment.
- `AddCqrsSourceGenerated` itself is trim-safe (dispatchers + notifications
  only); pair it with the generator-emitted `AddCqrsHandlersFrom*` for handlers.

## Trim status per behavior

Proven by this smoke (in the published graph, zero `IL2xxx`):

- `CqrsBroker`, `CommandDispatcher`, `QueryDispatcher`, `DispatcherBase`
  pipeline, source-generated `AddCqrs`.
- Deferred pump (`DeferAsync`/recovery/execute), `IDeferredEnvelopeFactory`
  registry, both envelope shapes, and a file-backed `IDeferredStore` (CQRS-39).

Inspected (no `MakeGenericType` / `Activator.CreateInstance` / `Type.GetType`
on the registry path), but NOT in the published graph — adopt then re-prove:

- Notifications fan-out, logging / error-handling / metrics / retry /
  rate-limit / circuit-breaker / transaction behaviors, in-memory caching +
  idempotency, result adapters (`ErrorOr`, `FluentResults` — community binaries,
  not analyzed here).
- Explicitly NOT AOT-ready: `Jeninnet.CQRS.Idempotency.EntityFrameworkCore`
  (EF Core change tracking; use compiled models — see its README) and anything
  built on the `Jeninnet.Validation` runtime rule engine.

Every new `IL2xxx` fails the build from day one; extend this smoke before
depending on an unproven behavior in a trimmed app.
