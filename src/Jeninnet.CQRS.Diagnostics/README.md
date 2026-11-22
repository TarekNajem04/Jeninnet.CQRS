# Jeninnet.CQRS.Diagnostics

OpenTelemetry tracing and metrics for the Jeninnet CQRS pipeline.

## Usage

```csharp
services.AddCqrsDiagnostics(); // or AddCqrsDiagnostics(o => o.EnableTracing = false)
```

The `DiagnosticsBehavior<TRequest, TResponse>` runs outermost (`PipelineOrder.DIAGNOSTICS`)
and produces, per dispatch:

- a `Dispatch` span on `ActivitySource` `Jeninnet.CQRS` with `cqrs.request` and
  `cqrs.response` tags and an Ok/Error status (exceptions are recorded on the span);
- a `dispatch.duration` histogram (ms) tagged with request type and outcome
  (`success`, `failure`, `cancelled`) on meter `Jeninnet.CQRS`;
- a `dispatch.errors` counter increment tagged with request and exception types.

Wire any OpenTelemetry SDK exporter (OTLP, console, Prometheus) to consume them.
With both options disabled the behavior is a single-await pass-through.