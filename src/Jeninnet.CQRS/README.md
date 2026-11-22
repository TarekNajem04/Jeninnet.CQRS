<div style="text-align:center;font-weight:bold;font-size:3em;color:Orange;padding-bottom: 20px;text-shadow: 4px 4px 15px red;">
Jeninnet.CQRS
</div>

# Jeninnet.CQRS

A lightweight, standalone .NET 10 class library implementing the core CQRS and mediator patterns.  
It provides:

- Strongly-typed **commands** and **queries** with handler dispatch.  
- A built-in **mediator** (split into command and query dispatchers).  
- **Pipeline behaviors** (metrics, error handling, validation, transactions) you opt into.  
- Pre- and post-processors for custom hooks.  
- Simple DI registration—no external scanners or packages.

---

## Table of Contents

1. [Getting Started](#getting-started)  
2. [Quick “How To”](#quick-how-to)  
3. [Project Structure & Key Sections](#project-structure--key-sections)  
4. [Code Templates](#code-templates)  
5. [Further Reading](#further-reading)  

---

## Getting Started

### Prerequisites

- .NET 10 SDK  
- C# 14 (file-scoped namespaces, primary constructors)  
- Your project uses Microsoft.Extensions.DependencyInjection and Microsoft.Extensions.Logging  

### Installation

1. Add the **Jeninnet.CQRS** project to your solution, or install via NuGet:

   ```bash
   dotnet add [YourProject] reference ../Jeninnet.CQRS/Jeninnet.CQRS.csproj
   ```

2. In your **Program.cs** (or Startup):

   ```csharp
   using Jeninnet.CQRS.Extensions;

   var builder = WebApplication.CreateBuilder(args);

   // 1. Add logging, DbContext, repositories, etc.
   builder.Services.AddLogging();
   builder.Services.AddDbContext<AppDbContext>(/* … */);

   // 2. Register CQRS core and choose behaviors
   builder.Services.AddAbeerAlShaamCqrs(opts =>
   {
       opts.AddMetricsBehavior      = true;
       opts.AddErrorHandlingBehavior = true;
       opts.AddValidationBehavior   = true;
       opts.AddTransactionBehavior  = false;
   });

   var app = builder.Build();
   app.MapControllers();
   app.Run();
   ```

---

## Quick “How To”

1. **Define a command or query**  
   - Commands implement `ICommand` (no return).  
   - Queries implement `IQuery<TResult>` (return `TResult`).  

2. **Implement a handler**  
   - `ICommandHandler<YourCommand>` for commands.  
   - `IQueryHandler<YourQuery,TResult>` for queries.  

3. **(Optional) Add validators, UoW, processors**  
   - `ISyncValidator<YourRequest>` for validation.  
   - `IUnitOfWork` for transaction scope.  
   - `IPreProcessor<TRequest>`/`IPostProcessor<TRequest,TResponse>` for custom hooks.  

4. **Choose pipeline behaviors**  
   - MetricsBehavior measures timing.  
   - ErrorHandlingBehavior logs exceptions.  
   - ValidationBehavior runs FluentValidation.  
   - TransactionBehavior wraps commands in a transaction.  

5. **Inject and dispatch**  
   ```csharp
   public class OrdersController : ControllerBase
   {
       private readonly ICommandDispatcher _commands;
       private readonly IQueryDispatcher   _queries;

       public OrdersController(
           ICommandDispatcher commands,
           IQueryDispatcher   queries)
       {
           _commands = commands;
           _queries  = queries;
       }

       [HttpPost("orders")]
       public async Task<IActionResult> CreateOrder(CreateOrderDto dto)
       {
           var cmd = new CreateOrderCommand(dto.CustomerId, dto.Items);
           await _commands.SendAsync(cmd);
           return Accepted();
       }

       [HttpGet("orders/{id}")]
       public async Task<OrderDetailsDto> GetOrder(int id)
       {
           var qry = new GetOrderByIdQuery(id);
           return await _queries.SendAsync<GetOrderByIdQuery, OrderDetailsDto>(qry);
       }
   }
   ```

### Multi-tenancy

`AddCqrsMultiTenancy` makes the tenant a first-class dispatch value. It registers an
`ITenantContext` backed by `AsyncLocal`, a tenant-qualified key composer, and strict
missing-tenant enforcement:

```csharp
services.AddCqrsMultiTenancy<HeaderTenantResolver>();
// or: services.AddCqrsMultiTenancy(provider => provider.GetRequiredService<IHttpContextAccessor>()
//                                                .HttpContext?.Request.Headers["x-tenant-id"]);

app.Use(async (http, next) =>
{
    var resolver = http.RequestServices.GetRequiredService<ITenantResolver>();
    var tenant = resolver.Resolve(http.RequestServices);

    using var scope = http.RequestServices.GetRequiredService<ITenantContext>().BeginScope(tenant!);
    await next();
});
```

- Cache, idempotency, rate-limit, and partitioned-concurrency keys are prefixed with the tenant,
  so two tenants using the same caller key never share a partition.
- `TransportEnvelope.TenantId` and the `x-tenant-id` header are populated from the ambient tenant
  by the request client, outbox relay, scheduler worker, and in-memory transport.
- Deferred, recurring, outbox, dead-letter, request, and scheduler rows persist the tenant. The
  EF package applies query filters so a scoped read cannot observe another tenant's rows.
- The default is strict: a dispatch without an ambient tenant throws
  `TenantRequiredException`. Set `MultiTenancyOptions.RequireTenant = false` for optional
  tenancy. Tenant provisioning, per-tenant connection strings, and tenant-aware consumer
  middleware are host responsibilities.

---

## Project Structure & Key Sections

```
Jeninnet.CQRS/
├── CommandDispatcher.cs
├── QueryDispatcher.cs
├── CqrsBroker.cs
├── IPipelineBehavior`2.cs
├── Unit.cs
├── ValidationBehavior`2.cs
├── ErrorHandlingBehavior`2.cs
├── MetricsBehavior`2.cs
├── TransactionBehavior`2.cs
│
├── Interfaces/
│   ├── ICommand.cs
│   ├── IQuery`1.cs
│   ├── ICommandHandler`1.cs
│   ├── IQueryHandler`2.cs
│   ├── ICommandDispatcher.cs
│   ├── IQueryDispatcher.cs
│   └── ICqrsBroker.cs
│
└── Extensions/
    ├── CqrsPipelineOptions.cs
    └── PipelineBehaviorServiceCollectionExtensions.cs
```

### Interfaces

- **ICommand** / **IQuery<TResult>**  
  Marker interfaces for write vs. read operations.  

- **ICommandHandler<TCommand>** / **IQueryHandler<TQuery,TResult>**  
  The contract your handlers implement.  

- **ICommandDispatcher** / **IQueryDispatcher**  
  Focused dispatchers for commands and queries.  

- **ICqrsBroker**  
  Composite façade combining both dispatchers.  

### Mediator Folder

- **CommandDispatcher** / **QueryDispatcher**  
  Resolve pre-processors, pipeline behaviors, handler, post-processors.  

- **Unit**  
  Sentinel for void‐return commands.  

- **IPipelineBehavior<TRequest,TResponse>**  
  Hook for cross‐cutting logic around each request.  

- **Built-In Behaviors**  
  Validation, error handling, metrics timing, transaction scope.  

### Extensions Folder

- **CqrsPipelineOptions**  
  Enables or disables each built-in behavior.  

- **PipelineBehaviorServiceCollectionExtensions**  
  Registers dispatchers and behaviors into `IServiceCollection`.

---

## Code Templates

### Command

```csharp
public sealed record CreateOrderCommand(int CustomerId, List<OrderItemDto> Items)
    : ICommand;
```

### Command Handler

```csharp
public sealed class CreateOrderHandler 
    : ICommandHandler<CreateOrderCommand>
{
    private readonly IOrderRepository _orders;
    public CreateOrderHandler(IOrderRepository orders) 
        => _orders = orders;

    public async Task HandleAsync(
        CreateOrderCommand cmd,
        CancellationToken ct = default)
    {
        var order = Order.Create(cmd.CustomerId, cmd.Items);
        await _orders.AddAsync(order, ct)
                     .ConfigureAwait(false);
    }
}
```

### Query

```csharp
public sealed record GetOrderByIdQuery(int OrderId)
    : IQuery<OrderDetailsDto>;
```

### Query Handler

```csharp
public sealed class GetOrderByIdHandler 
    : IQueryHandler<GetOrderByIdQuery, OrderDetailsDto>
{
    private readonly IReadModel _read;
    public GetOrderByIdHandler(IReadModel read) => _read = read;

    public async Task<OrderDetailsDto> HandleAsync(
        GetOrderByIdQuery qry,
        CancellationToken ct = default)
    {
        return await _read.FetchOrderAsync(qry.OrderId, ct)
                          .ConfigureAwait(false);
    }
}
```

### Validator (FluentValidation)

```csharp
public sealed class CreateOrderValidator 
    : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleForEach(x => x.Items)
            .SetValidator(new OrderItemValidator());
    }
}
```

### IUnitOfWork Implementation

```csharp
public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;
    public EfUnitOfWork(AppDbContext db) => _db = db;

    public Task BeginTransactionAsync(CancellationToken ct = default)
        => _db.Database.BeginTransactionAsync(ct);

    public Task CommitAsync(CancellationToken ct = default)
        => _db.Database.CurrentTransaction.CommitAsync(ct);

    public Task RollbackAsync(CancellationToken ct = default)
        => _db.Database.CurrentTransaction.RollbackAsync(ct);
}
```

---

## Target frameworks

The core package ships `net462`, `netstandard2.0`, `net10.0`, and `net11.0` assets. The legacy
legs use an internal polyfill layer (`IsExternalInit`, `CallerArgumentExpression`, `Lock`,
`TimeProvider`, `PriorityQueue`, and nullable annotations); `System.TimeProvider` is public in
the legacy assemblies because the core API already exposes it, while modern target frameworks
use the framework type. Caching, Idempotency, Resilience, Diagnostics, HealthChecks, Testing,
and Sagas follow the same shape. Transports and serialization are `netstandard2.0`+ only;
Validation, the result adapters, FluentValidation, EF Core, and the ASP.NET Core adapter remain
blocked by their dependencies (see the
[ADR registry](https://github.com/TarekNajem04/Jeninnet.CQRS/blob/main/docs/adr/README.md)). The net462 smoke is
`samples/Jeninnet.CQRS.Sample.NetFramework`.

## Further Reading

- Explore **pipeline behaviors**: chain custom decorators for logging, caching, throttling.  
- Extend **pre- and post-processors** for auditing or notifications.  
- Combine with **Domain Events** to update read models asynchronously.  
- Add **unit and integration tests** for each handler and behavior to ensure end-to-end correctness.
