# Jeninnet.CQRS.Contracts

Zero-dependency message contracts for producers and consumers that do not reference the CQRS
runtime. Target: `netstandard2.0`; no package references.

```csharp
using Jeninnet.CQRS;
using Jeninnet.CQRS.Interfaces;

public sealed record CreateOrder(string Id) : ICommand;
public sealed record GetOrder(string Id) : IQuery<OrderView>;
public sealed record OrderPlaced(string Id) : INotification;

[MessageVersion(2)]
public sealed record OrderShipped(string Id) : IEvent;
```

The package contains `ICommand`, `ICommand<T>`, `IQuery<T>`, `IStreamQuery<T>`, `INotification`,
`IEvent`, `Unit`, `[MessageVersion]`, and the `MessageHeaders` constants. Handler and dispatcher
interfaces stay in `Jeninnet.CQRS`; they are behavioral contracts that need DI and are covered by
`ADR-0009`.

## Migration

`Jeninnet.CQRS` references this package and forwards the moved types, so existing source and
compiled consumers keep resolving `Jeninnet.CQRS.Interfaces.*` and `Jeninnet.CQRS.Unit` without a
namespace change. Add a direct reference to `Jeninnet.CQRS.Contracts` only when a producer or
consumer repository should avoid the core runtime.
