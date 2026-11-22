//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

[TestClass]
public sealed class DispatcherTests {
    private IServiceProvider _provider = null!;

    [TestInitialize]
    public void Setup() {
        var services = new ServiceCollection();
        services.AddLogging();

        // 1. Register the concrete dispatchers required by CqrsBroker
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();

        // 2. Register the Broker itself
        services.AddTransient<ICqrsBroker, CqrsBroker>();

        // 3. Register the specific handlers for this test
        services.AddTransient<ICommandHandler<TestCommand, string>, TestHandler>();
        services.AddTransient<ICommandHandler<VoidTestCommand, Unit>, VoidTestHandler>();
        services.AddTransient<ICommandHandler<ErgonomicVoidTestCommand>, ErgonomicVoidTestHandler>();

        _provider = services.BuildServiceProvider();
    }

    [TestMethod]
    public async Task DispatchAsync_ShouldReturnExpectedResultAsync() {
        var broker = _provider.GetRequiredService<ICqrsBroker>();
        var command = new TestCommand("Hello");

        var result = await broker.DispatchAsync<TestCommand, string>(command, TestContext!.CancellationToken);

        MSTestAssertions.That(() => result.Should().Be("Processed: Hello"));
    }

    [TestMethod]
    public async Task VoidCommand_WithUnitHandler_DispatchesAsync() {
        // Arrange
        var broker = _provider.GetRequiredService<ICqrsBroker>();

        // Act
        await broker.DispatchAsync(new VoidTestCommand("hello"), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert: the handler ran; no exception is the contract for a void command.
        MSTestAssertions.That(() => true.Should().BeTrue());
    }

    [TestMethod]
    public async Task VoidCommand_WithErgonomicHandler_DispatchesAsync() {
        // Arrange
        var broker = _provider.GetRequiredService<ICqrsBroker>();

        // Act
        await broker.DispatchAsync(new ErgonomicVoidTestCommand("hello"), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => true.Should().BeTrue());
    }

    [TestMethod]
    public async Task MissingResultHandler_ThrowsWithRequestNameAsync() {
        // Arrange
        var broker = _provider.GetRequiredService<ICqrsBroker>();

        // Act
        var thrown = await CaptureAsync(() => broker.DispatchAsync<MissingResultCommand, string>(new MissingResultCommand(), TestContext!.CancellationToken)).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<InvalidOperationException>());
        MSTestAssertions.That(() => thrown!.Message.Should().Contain(nameof(MissingResultCommand)));
    }

    [TestMethod]
    public async Task ProtectedInvokeHandlerHelper_ReturnsTheHandlerResponseAsync() {
        // Arrange: a custom dispatcher exercises the protected helper directly.
        var handler = new TestHandler();

        // Act
        var result = await ExposedDispatcher.InvokeAsync(handler, new TestCommand("helper"), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => result.Should().Be("Processed: helper"));
    }

    [TestMethod]
    public async Task VoidCommand_WithoutHandler_ThrowsWithCommandNameAsync() {
        // Arrange
        var broker = _provider.GetRequiredService<ICqrsBroker>();

        // Act
        var thrown = await CaptureAsync(() => broker.DispatchAsync(new MissingVoidCommand(), TestContext!.CancellationToken)).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<InvalidOperationException>());
        MSTestAssertions.That(() => thrown!.Message.Should().Contain(nameof(MissingVoidCommand)));
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action) {
        try {
            await action().ConfigureAwait(false);

            return null;
        }
        catch(Exception exception) {
            return exception;
        }
    }

    public TestContext? TestContext { get; set; }

    private sealed record TestCommand(string Message) : ICommand<string>;

    private sealed class TestHandler : ICommandHandler<TestCommand, string> {
        public Task<string> HandleAsync(TestCommand request, CancellationToken cancellationToken = default)
            => Task.FromResult($"Processed: {request.Message}");
    }

    private sealed record VoidTestCommand(string Message) : ICommand;

    private sealed class VoidTestHandler : ICommandHandler<VoidTestCommand, Unit> {
        public Task<Unit> HandleAsync(VoidTestCommand request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Unit.Value);
    }

    private sealed record ErgonomicVoidTestCommand(string Message) : ICommand;

    private sealed class ErgonomicVoidTestHandler : ICommandHandler<ErgonomicVoidTestCommand> {
        public Task<Unit> HandleAsync(ErgonomicVoidTestCommand request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Unit.Value);
    }

    private sealed record MissingVoidCommand : ICommand;

    private sealed record MissingResultCommand : ICommand<string>;

    private sealed class ExposedDispatcher(IServiceProvider provider) : DispatcherBase(provider) {
        internal static Task<string> InvokeAsync(IHandler<TestCommand, string> handler, TestCommand command, CancellationToken cancellationToken) =>
            InvokeHandlerAsync<TestCommand, string, IHandler<TestCommand, string>>(handler, command, cancellationToken);
    }
}