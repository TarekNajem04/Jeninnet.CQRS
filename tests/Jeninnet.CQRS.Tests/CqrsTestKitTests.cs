//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

[TestClass]
public class CqrsTestKitTests {
    public TestContext? TestContext { get; set; }

    private static TestCqrsBroker RecordingBroker(Action<IServiceCollection>? configure = null) {
        var services = new ServiceCollection();
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddCqrsNotifications();
        configure?.Invoke(services);

        return new TestCqrsBroker(services.BuildServiceProvider().GetRequiredService<ICqrsBroker>());
    }

    [TestMethod]
    public async Task ShouldHaveDispatched_Passes_ForMatchingCommandAsync() {
        // Arrange
        var broker = RecordingBroker(s => s.AddTransient<ICommandHandler<TestCommand, string>, EchoHandler>());

        // Act
        await broker.DispatchAsync<TestCommand, string>(new TestCommand("Hi"), default);

        // Assert
        broker.ShouldHaveDispatched<TestCommand>(c => c.Message == "Hi");
    }

    [TestMethod]
    public async Task ShouldHaveDispatched_Failure_ListsActualMessagesAsync() {
        // Arrange
        var broker = RecordingBroker(s => s.AddTransient<ICommandHandler<TestCommand, string>, EchoHandler>());
        await broker.DispatchAsync<TestCommand, string>(new TestCommand("Hi"), default);

        // Act
        var failure = await CaptureAsync(() => Task.Run(() => broker.ShouldHaveDispatched<TestCommand>(c => c.Message == "Nope"), TestContext!.CancellationToken));

        // Assert: the failure names the expectation and the actual dispatched message.
        MSTestAssertions.That(() => failure.Should().NotBeNull());
        MSTestAssertions.That(() => failure!.Message.Should().Contain("TestCommand"));
        MSTestAssertions.That(() => failure!.Message.Should().Contain("Hi"));
    }

    [TestMethod]
    public async Task ShouldNotHaveDispatched_Passes_WhenNothingMatchesAsync() {
        // Arrange
        var broker = RecordingBroker(s => s.AddTransient<ICommandHandler<TestCommand, string>, EchoHandler>());

        // Act
        await broker.DispatchAsync<TestCommand, string>(new TestCommand("Hi"), default);

        // Assert
        broker.ShouldNotHaveDispatched<OtherCommand>();
        broker.ShouldNotHaveDispatched<TestCommand>(c => c.Message == "Nope");
    }

    [TestMethod]
    public async Task ShouldHavePublished_AndNotHavePublished_RoundTripAsync() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddCqrsNotifications();
        services.AddTransient<INotificationHandler<TestEvent>, TestEventHandler>();
        var broker = new TestCqrsBroker(services.BuildServiceProvider().GetRequiredService<ICqrsBroker>());

        // Act
        await broker.PublishAsync(new TestEvent("Ping"), default);

        // Assert
        broker.ShouldHavePublished<TestEvent>(e => e.Name == "Ping");
        broker.ShouldNotHavePublished<OtherEvent>();
    }

    [TestMethod]
    public async Task RecordingBroker_RecordsQueriesStreamsAndBatchesAsync() {
        // Arrange
        var broker = RecordingBroker(services => {
            services.AddTransient<ICommandHandler<TestCommand, string>, EchoHandler>();
            services.AddTransient<IQueryHandler<TestQuery, string>, TestQueryHandler>();
            services.AddTransient<IStreamQueryHandler<TestStreamQuery, int>, TestStreamHandler>();
        });

        // Act
        var query = await ((IQueryDispatcher)broker).DispatchAsync<TestQuery, string>(new TestQuery("query"), default).ConfigureAwait(false);
        var streamed = new List<int>();
        await foreach(var item in ((IQueryDispatcher)broker).StreamAsync<TestStreamQuery, int>(new TestStreamQuery(2), default).ConfigureAwait(false)) {
            streamed.Add(item);
        }

        var commandBatch = await broker.DispatchBatchAsync<TestCommand, string>([new TestCommand("a"), new TestCommand("b")], cancellationToken: TestContext!.CancellationToken).ConfigureAwait(false);
        var queryBatch = await broker.DispatchQueryBatchAsync<TestQuery, string>([new TestQuery("c")], cancellationToken: TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => query.Should().Be("Echo:query"));
        MSTestAssertions.That(() => streamed.Should().Equal([1, 2]));
        MSTestAssertions.That(() => broker.DispatchedQueries.Should().HaveCount(3));
        MSTestAssertions.That(() => broker.DispatchedCommands.Should().HaveCount(2));
        MSTestAssertions.That(() => commandBatch.IsSuccess.Should().BeTrue());
        MSTestAssertions.That(() => queryBatch.IsSuccess.Should().BeTrue());
    }

    [TestMethod]
    public async Task RecordingBroker_RecordsVoidCommandsAsync() {
        // Arrange
        var broker = RecordingBroker(services => services.AddTransient<ICommandHandler<VoidCommand>, VoidCommandHandler>());

        // Act
        await broker.DispatchAsync(new VoidCommand("void"), default).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => broker.DispatchedCommands.Should().HaveCount(1));
        MSTestAssertions.That(() => broker.DispatchedCommands[0].Should().BeOfType<VoidCommand>());
    }

    [TestMethod]
    public async Task PublishedAssertions_SupportNullMatchAndNegativeFailureAsync() {
        // Arrange
        var broker = RecordingBroker(services => services.AddTransient<INotificationHandler<TestEvent>, TestEventHandler>());
        await broker.PublishAsync(new TestEvent("Ping"), default).ConfigureAwait(false);

        // Act: a null match asserts the type; the negative assertion must fail and name the item.
        broker.ShouldHavePublished<TestEvent>();
        broker.ShouldNotHavePublished<TestEvent>(e => e.Name == "Nope");
        var failure = await CaptureAsync(() => Task.Run(() => broker.ShouldNotHavePublished<TestEvent>(), TestContext!.CancellationToken)).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => failure.Should().NotBeNull());
        MSTestAssertions.That(() => failure!.Message.Should().Contain("Ping"));
    }

    [TestMethod]
    public async Task RecordingBroker_RecordsVoidCommandBatchAsync() {
        // Arrange
        var broker = RecordingBroker(services => services.AddTransient<ICommandHandler<VoidCommand>, VoidCommandHandler>());

        // Act
        var result = await broker.DispatchBatchAsync<VoidCommand>([new VoidCommand("a"), new VoidCommand("b")], cancellationToken: TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => result.IsSuccess.Should().BeTrue());
        MSTestAssertions.That(() => broker.DispatchedCommands.Should().HaveCount(2));
    }

    [TestMethod]
    public async Task NegativeAssertions_ListActualItemsWhenTheyFailAsync() {
        // Arrange
        var broker = RecordingBroker(services => services.AddTransient<ICommandHandler<TestCommand, string>, EchoHandler>());
        await broker.DispatchAsync<TestCommand, string>(new TestCommand("Hi"), default).ConfigureAwait(false);

        // Act
        var dispatchedFailure = await CaptureAsync(() => Task.Run(() => broker.ShouldNotHaveDispatched<TestCommand>(), TestContext!.CancellationToken)).ConfigureAwait(false);
        var publishedFailure = await CaptureAsync(() => Task.Run(() => broker.ShouldHavePublished<TestEvent>(), TestContext!.CancellationToken)).ConfigureAwait(false);
        var behaviorFailure = await CaptureAsync(() => Task.Run(() => new BehaviorCallLog().ShouldHaveRunBehavior<SpyBehavior<TestCommand, string>>(), TestContext!.CancellationToken)).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => dispatchedFailure!.Message.Should().Contain("Hi"));
        MSTestAssertions.That(() => publishedFailure!.Message.Should().Contain("(nothing)"));
        MSTestAssertions.That(() => behaviorFailure!.Message.Should().Contain("(nothing)"));
    }

    [TestMethod]
    public async Task Describe_RendersNullEntriesAsync() {
        // Arrange
        var broker = RecordingBroker();
        broker.DispatchedCommands.Add(null!);

        // Act
        var failure = await CaptureAsync(() => Task.Run(() => broker.ShouldHaveDispatched<TestCommand>(), TestContext!.CancellationToken)).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => failure!.Message.Should().Contain("(null)"));
    }

    [TestMethod]
    public async Task ShouldHaveRunBehavior_Passes_WhenSpyRunsInPipelineAsync() {
        // Arrange
        var log = new BehaviorCallLog();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(SpyBehavior<,>));
        services.AddTransient<ICommandHandler<TestCommand, string>, EchoHandler>();
        var broker = services.BuildServiceProvider().GetRequiredService<ICqrsBroker>();

        // Act
        await broker.DispatchAsync<TestCommand, string>(new TestCommand("Hi"), default);

        // Assert
        log.ShouldHaveRunBehavior<SpyBehavior<TestCommand, string>>();
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> act) {
        try {
            await act();
            return null;
        }
        catch(Exception ex) {
            return ex;
        }
    }

    private sealed record TestCommand(string Message) : ICommand<string>;

    private sealed record OtherCommand(string Message) : ICommand<string>;

    private sealed record VoidCommand(string Message) : ICommand;

    private sealed record TestQuery(string Value) : IQuery<string>;

    private sealed record TestStreamQuery(int Count) : IStreamQuery<int>;

    private sealed record TestEvent(string Name) : INotification;

    private sealed record OtherEvent(string Name) : INotification;

    private sealed class EchoHandler : ICommandHandler<TestCommand, string> {
        public Task<string> HandleAsync(TestCommand request, CancellationToken cancellationToken)
            => Task.FromResult($"Echo:{request.Message}");
    }

    private sealed class TestEventHandler : INotificationHandler<TestEvent> {
        public Task HandleAsync(TestEvent notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class VoidCommandHandler : ICommandHandler<VoidCommand> {
        public Task<Unit> HandleAsync(VoidCommand command, CancellationToken cancellationToken) => Task.FromResult(Unit.Value);
    }

    private sealed class TestQueryHandler : IQueryHandler<TestQuery, string> {
        public Task<string> HandleAsync(TestQuery query, CancellationToken cancellationToken) => Task.FromResult($"Echo:{query.Value}");
    }

    private sealed class TestStreamHandler : IStreamQueryHandler<TestStreamQuery, int> {
        public async IAsyncEnumerable<int> HandleAsync(TestStreamQuery query, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            for(var index = 1; index <= query.Count; index++) {
                await Task.Yield();
                yield return index;
            }
        }
    }
}
