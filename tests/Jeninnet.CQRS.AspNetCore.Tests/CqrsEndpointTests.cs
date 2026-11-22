//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.AspNetCore.Tests;

/// <summary>
/// End-to-end tests for CQRS-32 against a real TestServer host: command/void/query/stream
/// round trips, JSON binding from body/route/query, and ProblemDetails mapping.
/// </summary>
[TestClass]
public class CqrsEndpointTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task CommandWithResult_RoundTripsFromBodyAsync() {
        // Arrange
        await using var app = await StartAppAsync();
        using var client = app.GetTestClient();

        // Act
        var response = await client.PostAsJsonAsync("/widgets", new { name = "body-name", quantity = 2 }, TestContext!.CancellationToken);
        var widget = await response.Content.ReadFromJsonAsync<Widget>(TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => response.StatusCode.Should().Be(HttpStatusCode.OK));
        var result = widget!;
        MSTestAssertions.That(() => result.Name.Should().Be("body-name"));
        MSTestAssertions.That(() => result.Quantity.Should().Be(2));
    }

    [TestMethod]
    public async Task VoidCommand_RoundTripsFromRoute_WithNoBodyAsync() {
        // Arrange
        await using var app = await StartAppAsync();
        using var client = app.GetTestClient();
        var calls = app.Services.GetRequiredService<List<string>>();

        // Act
        var response = await client.DeleteAsync("/widgets/route-id", TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => response.StatusCode.Should().Be(HttpStatusCode.NoContent));
        MSTestAssertions.That(() => calls.Should().Equal(["route-id"]));
    }

    [TestMethod]
    public async Task Query_RoundTripsFromQueryString_AndRouteWinsAsync() {
        // Arrange
        await using var app = await StartAppAsync();
        using var client = app.GetTestClient();

        // Act
        var fromQuery = await client.GetFromJsonAsync<Widget>("/widgets?name=query-name", TestContext!.CancellationToken);
        var routeWins = await client.GetFromJsonAsync<Widget>("/widgets/route-name?name=ignored", TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => fromQuery!.Name.Should().Be("query-name"));
        MSTestAssertions.That(() => routeWins!.Name.Should().Be("route-name"));
    }

    [TestMethod]
    public async Task Stream_RoundTripsAsJsonArrayAsync() {
        // Arrange
        await using var app = await StartAppAsync();
        using var client = app.GetTestClient();

        // Act
        var response = await client.GetAsync("/widgets/stream?count=3", TestContext!.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => response.StatusCode.Should().Be(HttpStatusCode.OK));
        MSTestAssertions.That(() => response.Content.Headers.ContentType?.MediaType.Should().Be("application/json"));
        MSTestAssertions.That(() => body.Should().Be("[0,1,2]"));
    }

    [TestMethod]
    public async Task ValidationFailure_Returns400ProblemDetailsAsync() {
        // Arrange
        await using var app = await StartAppAsync();
        using var client = app.GetTestClient();

        // Act
        var response = await client.PostAsJsonAsync("/widgets", new { name = "", quantity = 1 }, TestContext!.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext!.CancellationToken);
        using var document = JsonDocument.Parse(body);

        // Assert
        MSTestAssertions.That(() => response.StatusCode.Should().Be(HttpStatusCode.BadRequest));
        MSTestAssertions.That(() => response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json"));

        var errors = document.RootElement.GetProperty("errors");
        MSTestAssertions.That(() => errors.GetProperty("Name")[0].GetString().Should().Contain("Name is required."));
    }

    [TestMethod]
    public async Task UnhandledFailure_Returns500ProblemDetailsAsync() {
        // Arrange
        await using var app = await StartAppAsync();
        using var client = app.GetTestClient();

        // Act
        var response = await client.PostAsJsonAsync("/explode", new { }, TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => response.StatusCode.Should().Be(HttpStatusCode.InternalServerError));
        MSTestAssertions.That(() => response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json"));
    }

    [TestMethod]
    public async Task CustomExceptionHandler_IsHonoredAsync() {
        // Arrange
        await using var app = await StartAppAsync(options => options.ExceptionHandler = static (_, _) => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "mapped"));
        using var client = app.GetTestClient();

        // Act
        var response = await client.PostAsJsonAsync("/explode", new { }, TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => response.StatusCode.Should().Be(HttpStatusCode.Conflict));
    }

    private static async Task<WebApplication> StartAppAsync(Action<CqrsEndpointOptions>? configure = null) {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<List<string>>();
        builder.Services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        builder.Services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        builder.Services.AddTransient<ICqrsBroker, CqrsBroker>();
        builder.Services.AddTransient<ICommandHandler<CreateWidget, Widget>, CreateWidgetHandler>();
        builder.Services.AddTransient<ICommandHandler<DeleteWidget>, DeleteWidgetHandler>();
        builder.Services.AddTransient<ICommandHandler<ExplodeCommand>, ExplodeCommandHandler>();
        builder.Services.AddTransient<IQueryHandler<GetWidget, Widget>, GetWidgetHandler>();
        builder.Services.AddTransient<IStreamQueryHandler<StreamWidgets, int>, StreamWidgetsHandler>();
        builder.Services.AddTransient<ISyncValidator<CreateWidget>, CreateWidgetValidator>();
        builder.Services.AddCqrsValidationBehavior();
        builder.Services.AddCqrsEndpoints(configure);

        var app = builder.Build();
        app.MapCqrsCommand<CreateWidget, Widget>("/widgets", HttpMethods.Post);
        app.MapCqrsCommand<DeleteWidget>("/widgets/{Name}", HttpMethods.Delete);
        app.MapCqrsQuery<GetWidget, Widget>("/widgets");
        app.MapCqrsQuery<GetWidget, Widget>("/widgets/{Name}");
        app.MapCqrsStream<StreamWidgets, int>("/widgets/stream");
        app.MapCqrsCommand<ExplodeCommand>("/explode", HttpMethods.Post);
        await app.StartAsync();

        return app;
    }

    private sealed record Widget(string Name, int Quantity);

    private sealed record CreateWidget(string Name, int Quantity) : ICommand<Widget>;

    private sealed record DeleteWidget(string Name) : ICommand;

    private sealed record GetWidget(string Name) : IQuery<Widget>;

    private sealed record StreamWidgets(int Count) : IStreamQuery<int>;

    private sealed record ExplodeCommand : ICommand;

    private sealed class CreateWidgetHandler : ICommandHandler<CreateWidget, Widget> {
        public Task<Widget> HandleAsync(CreateWidget command, CancellationToken cancellationToken) =>
            Task.FromResult(new Widget(command.Name, command.Quantity));
    }

    private sealed class DeleteWidgetHandler(List<string> calls) : ICommandHandler<DeleteWidget> {
        public Task<Unit> HandleAsync(DeleteWidget command, CancellationToken cancellationToken) {
            calls.Add(command.Name);

            return Task.FromResult(Unit.Value);
        }
    }

    private sealed class ExplodeCommandHandler : ICommandHandler<ExplodeCommand> {
        public Task<Unit> HandleAsync(ExplodeCommand command, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class GetWidgetHandler : IQueryHandler<GetWidget, Widget> {
        public Task<Widget> HandleAsync(GetWidget query, CancellationToken cancellationToken) =>
            Task.FromResult(new Widget(query.Name, 0));
    }

    private sealed class StreamWidgetsHandler : IStreamQueryHandler<StreamWidgets, int> {
        public async IAsyncEnumerable<int> HandleAsync(StreamWidgets query, [EnumeratorCancellation] CancellationToken cancellationToken) {
            for(var index = 0; index < query.Count; index++) {
                yield return index;
                await Task.Yield();
            }
        }
    }

    private sealed class CreateWidgetValidator : ISyncValidator<CreateWidget> {
        public ValidationResult Validate(CreateWidget instance) =>
            string.IsNullOrWhiteSpace(instance.Name)
                ? ValidationResult.FromErrors([new ValidationError("Name", "Name is required.", "NAME_REQUIRED")])
                : ValidationResult.Success;

        public ValidationResult Validate(CreateWidget instance, IEnumerable<string> ruleSets) => Validate(instance);
    }
}
