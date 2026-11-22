# Jeninnet.CQRS.Analyzers

Roslyn compile-time misuse analyzers for Jeninnet.CQRS handlers. No code fixes in v1:
every diagnostic names the fix; automatic fixes are deliberately withheld until they can
be proven false-positive-free.

| ID | Severity | Title |
|---|---|---|
| `CQRS0001` | Warning | Duplicate handler: two handlers claim the same message; registration order decides. |
| `CQRS0002` | Warning | `CancellationToken` parameter is never forwarded to the awaited call. |
| `CQRS0003` | Warning | One handler class claims more than one message; split it. |

Only fully-closed handler contracts participate (`ICommandHandler<>`, `ICommandHandler<,>`,
`IQueryHandler<,>`, `INotificationHandler<>` in `Jeninnet.CQRS.Interfaces`). Open-generic
handler bases never report. A parameter literally named `_` opts out of `CQRS0002`.