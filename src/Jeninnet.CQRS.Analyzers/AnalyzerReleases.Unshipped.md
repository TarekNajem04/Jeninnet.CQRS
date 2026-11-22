### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
CQRS0001 | Usage | Warning | Duplicate handler: two handlers claim the same message; registration order decides
CQRS0002 | Usage | Warning | CancellationToken parameter is never forwarded to the awaited call
CQRS0003 | Usage | Warning | One handler class claims more than one message; split it