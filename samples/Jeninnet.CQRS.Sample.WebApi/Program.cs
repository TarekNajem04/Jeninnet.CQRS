//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//

var builder = WebApplication.CreateBuilder(args);

// T016: Configure WebApi sample to use CQRS pipeline via options
builder.Services.AddCqrsByReflection(
    options =>
        options.PipelineBehaviors = new PipelineBehaviorsOptions(
            EnableMetrics: true,
            EnableErrorHandling: true,
            EnableValidation: true,
            EnableLogging: true
        ));

// CQRS-11: durable idempotency (EF Core + SQLite) instead of the in-memory default.
builder.Services.AddCqrsEfIdempotencyStore(
    db => db.UseSqlite("Data Source=idempotency.db"),
    store => store.TimeToLive = TimeSpan.FromDays(7));

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddCqrsHealthChecks();

// CQRS-66: optional broker-free transports. The default stays in-process; set
// Cqrs:Transport to Http or SignalR in appsettings to exercise one.
var transport = builder.Configuration["Cqrs:Transport"];

if(string.Equals(transport, "Http", StringComparison.OrdinalIgnoreCase)) {
    builder.Services.AddCqrsHttpTransport(options => {
        options.Endpoint = builder.Configuration["Cqrs:Http:Endpoint"] ?? "http://localhost:5041/messages";
        options.ApiKey = builder.Configuration["Cqrs:Http:ApiKey"];
        options.HmacSecret = builder.Configuration["Cqrs:Http:HmacSecret"];
    });
} else if(string.Equals(transport, "SignalR", StringComparison.OrdinalIgnoreCase)) {
    builder.Services.AddCqrsSignalR(options =>
        options.HubUrl = builder.Configuration["Cqrs:SignalR:HubUrl"] ?? "http://localhost:5041/cqrs");
}

// The relay hub is always mapped so the sample doubles as a SignalR server demo.
builder.Services.AddSignalR();

var app = builder.Build();

if(app.Environment.IsDevelopment()) {
    app.MapOpenApi();
}

// CQRS-11: create the idempotency schema for the SQLite demo database.
await using(var scope = app.Services.CreateAsyncScope()) {
    await using var db = await scope.ServiceProvider
        .GetRequiredService<IDbContextFactory<IdempotencyDbContext>>()
        .CreateDbContextAsync()
        .ConfigureAwait(false);
    await db.Database.EnsureCreatedAsync().ConfigureAwait(false);
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.MapCqrsSignalRHub("/cqrs");
app.MapHealthChecks("/healthz");

await app.RunAsync();
