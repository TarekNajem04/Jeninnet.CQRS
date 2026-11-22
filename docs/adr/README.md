# Architecture Decision Records

Every capability we do not ship, every framework/TFM boundary, and every dependency choice
that limits reach is recorded here. An ADR is a decision record, not a TODO: it must state the
context, the decision, the consequences, and the concrete event plus date that would reopen it.

Use [TEMPLATE.md](TEMPLATE.md) for new records. Number them sequentially, keep the status and
dates current, and add the row to this registry in the same commit.

## Registry

| ADR | Decision | Status | Date | Revisit |
|---|---|---|---|---|
| [ADR-0001](ADR-0001-validation-netstandard2.md) | Validation package stays off `netstandard2.0` | Accepted (blocked) | 2026-11-18 | 2026-12-31 |
| [ADR-0002](ADR-0002-results-netstandard2.md) | Result adapters stay off `netstandard2.0` | Accepted (blocked transitively) | 2026-11-18 | 2026-12-31 |
| [ADR-0003](ADR-0003-fluentvalidation-netstandard2.md) | FluentValidation bridge stays off `netstandard2.0` | Accepted (vendor-blocked) | 2026-11-18 | 2026-12-31 |
| [ADR-0004](ADR-0004-efcore-netstandard2.md) | EF Core stores stay `net11.0`-only | Accepted (vendor-blocked) | 2026-11-18 | 2026-12-31 |
| [ADR-0005](ADR-0005-aspnetcore-netstandard2.md) | ASP.NET Core endpoint mapping stays on modern TFMs | Accepted (framework-blocked) | 2026-11-18 | Framework boundary |
| [ADR-0006](ADR-0006-net462-dispatch-boundary.md) | `net462` reach is limited to dispatch/observability | Accepted | 2026-11-18 | 2026-12-31 |
| [ADR-0007](ADR-0007-gcs-claim-check-deferred.md) | Google Cloud Storage claim-check provider is deferred | Accepted | 2026-11-19 | 2027-03-31 |
| [ADR-0008](ADR-0008-cold-storage-export-deferred.md) | Cold-storage export is deferred | Accepted | 2026-11-19 | 2027-03-31 |
| [ADR-0009](ADR-0009-handler-contracts-stay-in-core.md) | Handler and dispatcher interfaces stay in core | Accepted | 2026-11-19 | 2027-03-31 |
| [ADR-0010](ADR-0010-signalr-modern-tfms.md) | SignalR transport stays on modern TFMs | Accepted | 2026-11-19 | 2027-03-31 |
| [ADR-0011](ADR-0011-mqttnet-4-netstandard.md) | MQTT stays on MQTTnet 4.3 for `netstandard2.0` reach | Accepted | 2026-11-19 | 2027-03-31 |

## Quarterly review

The internal parity audit checklist is the re-audit source. During each review, check the registry for records
whose revisit date has passed, re-evaluate the dependency/TFM evidence, and mark superseded
records instead of deleting them.
