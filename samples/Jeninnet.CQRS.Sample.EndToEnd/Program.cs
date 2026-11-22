//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//

var builder = Host.CreateApplicationBuilder(args);

// Generated registration plus the four composed behaviors. Retry options bind
// from appsettings.json through IOptions (see RetryBehaviorOptions section).
builder.Services.AddCqrs();
builder.Services.AddCqrsValidationBehavior();
builder.Services.AddCqrsResilienceBehavior(builder.Configuration);
builder.Services.AddCqrsCachingBehavior();
builder.Services.AddCqrsIdempotencyBehavior();
builder.Services.AddTransient<ISyncValidator<CreateOrderCommand>, CreateOrderValidator>();

using var host = builder.Build();
var broker = host.Services.GetRequiredService<ICqrsBroker>();

Console.WriteLine("=== Jeninnet CQRS end to end ===");

// 1. Validation failure: the handler must never run.
var validations = 0;
try {
    await broker.DispatchAsync<CreateOrderCommand, string>(new CreateOrderCommand(string.Empty), default);
    validations++;
}
catch(ValidationException exception) {
    Console.WriteLine($"[1/4] Validation failed as expected: {exception.Message}");
}

MSTestAssertions.That(() => validations.Should().Be(0));
MSTestAssertions.That(() => CreateOrderHandler.Executions.Should().Be(0));

// 2. Retry success: the handler fails twice, then succeeds (RetryCount comes from appsettings.json).
var receipt = await broker.DispatchAsync<FlakyPingCommand, string>(new FlakyPingCommand("hello"), default);
Console.WriteLine($"[2/4] Retry succeeded after {FlakyPingHandler.Attempts} attempts: {receipt}");
MSTestAssertions.That(() => receipt.Should().Be("pong:hello"));
MSTestAssertions.That(() => FlakyPingHandler.Attempts.Should().Be(3));

// 3. Cache hit: the query handler runs once for two identical dispatches.
var first = await broker.DispatchAsync<GetCatalogQuery, string>(new GetCatalogQuery("books"), default);
var second = await broker.DispatchAsync<GetCatalogQuery, string>(new GetCatalogQuery("books"), default);
Console.WriteLine($"[3/4] Cache hit: handler ran {CatalogHandler.Executions} time(s).");
MSTestAssertions.That(() => first.Should().Be(second));
MSTestAssertions.That(() => CatalogHandler.Executions.Should().Be(1));

// 4. Idempotent replay: the same key twice runs the handler once.
var orderKey = "order-42";
var placed = await broker.DispatchAsync<PlaceOrderCommand, string>(new PlaceOrderCommand(orderKey), default);
var replayed = await broker.DispatchAsync<PlaceOrderCommand, string>(new PlaceOrderCommand(orderKey), default);
Console.WriteLine($"[4/4] Idempotent replay: handler ran {PlaceOrderHandler.Executions} time(s).");
MSTestAssertions.That(() => replayed.Should().Be(placed));
MSTestAssertions.That(() => PlaceOrderHandler.Executions.Should().Be(1));

Console.WriteLine("=== All four flows verified ===");

namespace Jeninnet.CQRS.Sample.EndToEnd {
    public sealed record CreateOrderCommand(string Customer) : ICommand<string>;

    public sealed class CreateOrderValidator : ISyncValidator<CreateOrderCommand> {
        public ValidationResult Validate(CreateOrderCommand instance) {
            if(!string.IsNullOrWhiteSpace(instance.Customer)) {
                return ValidationResult.Success;
            }

            return ValidationResult.FromErrors([
                new ValidationError("Customer", "Customer is required.", "CUSTOMER_REQUIRED"),
            ]);
        }

        public ValidationResult Validate(CreateOrderCommand instance, IEnumerable<string> ruleSets) => Validate(instance);
    }

    public sealed class CreateOrderHandler : ICommandHandler<CreateOrderCommand, string> {
        public static int Executions { get; private set; }

        public Task<string> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken) {
            Executions++;
            return Task.FromResult($"order-for:{command.Customer}");
        }
    }

    public sealed record FlakyPingCommand(string Name) : ICommand<string>;

    public sealed class FlakyPingHandler : ICommandHandler<FlakyPingCommand, string> {
        public static int Attempts { get; private set; }

        public Task<string> HandleAsync(FlakyPingCommand command, CancellationToken cancellationToken) {
            Attempts++;

            if(Attempts < 3) {
                throw new InvalidOperationException($"Transient failure {Attempts}.");
            }

            return Task.FromResult($"pong:{command.Name}");
        }
    }

    public sealed record GetCatalogQuery(string Category) : IQuery<string>, ICacheable<string> {
        public string CacheKey => $"catalog:{Category}";
        public MemoryCacheEntryOptions? CacheOptions => new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };
    }

    public sealed class CatalogHandler : IQueryHandler<GetCatalogQuery, string> {
        public static int Executions { get; private set; }

        public Task<string> HandleAsync(GetCatalogQuery query, CancellationToken cancellationToken) {
            Executions++;
            return Task.FromResult($"catalog:{query.Category}");
        }
    }

    public sealed record PlaceOrderCommand(string OrderKey) : IIdempotentCommand<string> {
        public string IdempotencyKey => OrderKey;
    }

    public sealed class PlaceOrderHandler : ICommandHandler<PlaceOrderCommand, string> {
        public static int Executions { get; private set; }

        public Task<string> HandleAsync(PlaceOrderCommand command, CancellationToken cancellationToken) {
            Executions++;
            return Task.FromResult($"placed:{command.OrderKey}");
        }
    }
}