//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

[TestClass]
public class ProcessorTests {
    private static readonly string[] _expected = new[] { "Pre", "Handle", "Post" };

    [TestMethod]
    public async Task DispatchAsync_ShouldExecutePreAndPostProcessorsAsync() {
        // Arrange
        var services = new ServiceCollection();
        var tracker = new List<string>();

        services.AddSingleton(tracker);
        services.AddTransient<ICommandHandler<TestCommand, Unit>, TrackedHandler>();
        services.AddTransient<IPreProcessor<TestCommand>, TrackedPreProcessor>();
        services.AddTransient<IPostProcessor<TestCommand, Unit>, TrackedPostProcessor>();

        // Manual registration for this specific test
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ICommandDispatcher>();

        // Act
        await dispatcher.DispatchAsync(new TestCommand("Run"), TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => tracker.Should().Equal(_expected));
    }

    // Helpers
    private sealed record TestCommand(string Message) : ICommand;

    private sealed class TrackedPreProcessor(List<string> t) : IPreProcessor<TestCommand> {
        public Task ProcessAsync(TestCommand r, CancellationToken ct) { t.Add("Pre"); return Task.CompletedTask; }
    }

    private sealed class TrackedHandler(List<string> t) : ICommandHandler<TestCommand, Unit> {
        public Task<Unit> HandleAsync(TestCommand r, CancellationToken ct) { t.Add("Handle"); return Task.FromResult(Unit.Value); }
    }

    private sealed class TrackedPostProcessor(List<string> t) : IPostProcessor<TestCommand, Unit> {
        public Task ProcessAsync(TestCommand r, Unit res, CancellationToken ct) { t.Add("Post"); return Task.CompletedTask; }
    }

    public TestContext? TestContext { get; set; }
}
