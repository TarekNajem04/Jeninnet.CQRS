//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.IntegrationTests;

[TestClass]
public class SourceGeneratorTests {
    [TestMethod]
    public void AddCqrs_ShouldAutomaticallyRegisterHandlers_FromSourceGenerator() {
        // Arrange
        var services = new ServiceCollection();

        // This is the method our Source Generator provides/augments
        // We test that it actually registers handlers in this assembly
        //services.AddCqrs(options => options.PipelineBehaviors.EnableCaching = true);
        services.AddCqrs();

        var provider = services.BuildServiceProvider();

        // Act & Assert
        // We try to resolve a handler that exists in this test project
        var handler = provider.GetService<ICommandHandler<IntegrationTestCommand, string>>();

        MSTestAssertions.That(() => handler.Should().NotBeNull("The Source Generator failed to register the ICommandHandler."));
        MSTestAssertions.That(() => handler.Should().BeAssignableTo<IntegrationTestHandler>());
    }

    [TestMethod]
    public async Task AddCqrs_ShouldResolveStreamHandlers_FromSourceGeneratorAsync() {
        // Arrange
        var services = new ServiceCollection();
        services.AddCqrs();
        var provider = services.BuildServiceProvider();

        // Act
        var handlers = provider.GetServices<IStreamQueryHandler<IntegrationTestFeed, string>>().ToList();
        var consumed = new List<string>();
        await foreach(var item in provider.GetRequiredService<ICqrsBroker>().StreamAsync<IntegrationTestFeed, string>(new IntegrationTestFeed("Ping"), default)) {
            consumed.Add(item);
        }

        // Assert: discovered by the generator, streamed end-to-end through the broker.
        MSTestAssertions.That(() => handlers.Should().HaveCount(1));
        MSTestAssertions.That(() => consumed.Should().Equal(["Ping-1", "Ping-2"]));
    }

    [TestMethod]
    public async Task AddCqrs_ShouldResolveNotificationHandlers_FromSourceGeneratorAsync() {
        // Arrange
        var services = new ServiceCollection();

        // AddCqrs is source-generated for this assembly: the notification handler below
        // must be discovered without any reflection scan.
        services.AddCqrs();
        services.AddCqrsNotifications();
        services.AddSingleton<IntegrationCallLog>();

        var provider = services.BuildServiceProvider();

        // Act: resolve through the container, then publish end-to-end through the broker.
        var handlers = provider.GetServices<INotificationHandler<IntegrationTestEvent>>().ToList();
        await provider.GetRequiredService<ICqrsBroker>().PublishAsync(new IntegrationTestEvent("Ping"), default);

        // Assert
        MSTestAssertions.That(() => handlers.Should().NotBeEmpty());
        MSTestAssertions.That(() => provider.GetRequiredService<IntegrationCallLog>().Calls.Should().Equal(["got:Ping"]));
    }

    [TestMethod]
    public async Task AddCqrs_ShouldDispatchConventionHandler_FromSourceGeneratorAsync() {
        // Arrange: the convention handler below has no interface; the generator must emit
        // an adapter (AOT-safe, no reflection).
        var services = new ServiceCollection();
        services.AddCqrs();
        services.AddSingleton<IntegrationCallLog>();
        var provider = services.BuildServiceProvider();

        // Act
        await provider.GetRequiredService<ICqrsBroker>().DispatchAsync(new IntegrationConventionCommand("Ping"), default);

        // Assert
        MSTestAssertions.That(() => provider.GetRequiredService<IntegrationCallLog>().Calls.Should().Equal(["convention:Ping"]));
    }

    [TestMethod]
    public async Task AddCqrs_ShouldDispatchVoidCommand_FromSourceGeneratorAsync() {
        // Arrange: the void handler below uses the ergonomic one-generic form;
        // the generator registers it and the dispatcher fallback resolves it.
        var services = new ServiceCollection();
        services.AddCqrs();
        services.AddSingleton<IntegrationCallLog>();
        var provider = services.BuildServiceProvider();

        // Act
        await provider.GetRequiredService<ICqrsBroker>().DispatchAsync(new IntegrationTestVoidCommand("Ping"), default);

        // Assert
        MSTestAssertions.That(() => provider.GetRequiredService<IntegrationCallLog>().Calls.Should().Equal(["void:Ping"]));
    }
}

// These classes exist only for the Generator to find during the test
public record IntegrationTestCommand(string Name) : ICommand<string>;

public class IntegrationTestHandler : ICommandHandler<IntegrationTestCommand, string> {
    public Task<string> HandleAsync(IntegrationTestCommand request, CancellationToken cancellationToken = default)
        => Task.FromResult($"Hello {request.Name}");
}

public record IntegrationTestEvent(string Name) : INotification;

public sealed class IntegrationCallLog {
    public List<string> Calls { get; } = [];
}

public sealed class IntegrationTestEventSubscriber(IntegrationCallLog log) : INotificationHandler<IntegrationTestEvent> {
    public Task HandleAsync(IntegrationTestEvent notification, CancellationToken cancellationToken) {
        log.Calls.Add($"got:{notification.Name}");
        return Task.CompletedTask;
    }
}

public sealed record IntegrationTestFeed(string Prefix) : IStreamQuery<string>;

public sealed class IntegrationTestFeedHandler : IStreamQueryHandler<IntegrationTestFeed, string> {
    public async IAsyncEnumerable<string> HandleAsync(IntegrationTestFeed query, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        yield return $"{query.Prefix}-1";
        await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        yield return $"{query.Prefix}-2";
    }
}

public sealed record IntegrationTestVoidCommand(string Name) : ICommand;

public sealed class IntegrationTestVoidHandler(IntegrationCallLog log) : ICommandHandler<IntegrationTestVoidCommand> {
    public Task<Unit> HandleAsync(IntegrationTestVoidCommand command, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        log.Calls.Add($"void:{command.Name}");
        return Task.FromResult(Unit.Value);
    }
}
public sealed record IntegrationConventionCommand(string Name) : ICommand;

public sealed class IntegrationConventionHandler(IntegrationCallLog log) {
    public Task HandleAsync(IntegrationConventionCommand command) {
        log.Calls.Add($"convention:{command.Name}");

        return Task.CompletedTask;
    }
}