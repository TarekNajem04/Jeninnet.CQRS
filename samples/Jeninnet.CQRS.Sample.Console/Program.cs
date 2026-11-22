//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
// 1. Setup the Host and Dependency Injection
var builder = Host.CreateApplicationBuilder(args);

// 2. Register CQRS via the Source Generator
// This replaces manual registration. It finds AddProductHandler 
// and GetProductHandler automatically because they are in this assembly.
builder.Services.AddCqrs()
       .AddCqrsErrorHandlingBehavior()
       .AddCqrsMetricsBehavior()
       ;

using var host = builder.Build();

// 3. Get the Broker (The entry point to the system)
var broker = host.Services.GetRequiredService<ICqrsBroker>();

System.Console.WriteLine("=== Jeninnet CQRS Sample Console ===\n");

// 4. STEP 1: Execute a Command (WRITE)
System.Console.WriteLine("Executing Command: Adding a new product...");
var command = new AddProductCommand("Gaming Laptop", 1500.00m);
var productId = await broker.DispatchAsync<AddProductCommand, int>(command);

System.Console.WriteLine($"Result: Product created with ID: {productId}\n");

// 5. STEP 2: Execute a Query (READ)
System.Console.WriteLine($"Executing Query: Fetching product {productId}...");
var query = new GetProductQuery(productId);
var product = await broker.DispatchAsync<GetProductQuery, Product?>(query);

if(product is not null) {
    System.Console.WriteLine($"Result: Found {product.Name} ($ {product.Price})");
}

System.Console.WriteLine("\n=== Demonstration Complete ===");