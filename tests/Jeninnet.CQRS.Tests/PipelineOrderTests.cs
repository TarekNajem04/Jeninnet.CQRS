//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>Mutation-characterization tests for ordered pipeline execution.</summary>
[TestClass]
public sealed class PipelineOrderTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task OrderedBehaviors_RunFromLowestOrderToHighestAsync() {
        // Arrange
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddSingleton<ICqrsBroker, CqrsBroker>();
        services.AddTransient<ICommandHandler<OrderedCommand, int>, OrderedCommandHandler>();
        services.AddTransient<IPipelineBehavior<OrderedCommand, int>>(_ => new OrderedBehavior(calls, 20, "second"));
        services.AddTransient<IPipelineBehavior<OrderedCommand, int>>(_ => new OrderedBehavior(calls, 10, "first"));

        var broker = services.BuildServiceProvider().GetRequiredService<ICqrsBroker>();

        // Act
        var result = await broker.DispatchAsync<OrderedCommand, int>(new OrderedCommand(), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => result.Should().Be(1));
        MSTestAssertions.That(() => calls.Should().Equal(["first", "second"]));
    }

    private sealed record OrderedCommand : ICommand<int>;

    private sealed class OrderedCommandHandler : ICommandHandler<OrderedCommand, int> {
        public Task<int> HandleAsync(OrderedCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(1);
    }

    private sealed class OrderedBehavior(List<string> calls, int order, string name) : IOrderedPipelineBehavior<OrderedCommand, int> {
        public int Order => order;

        public async Task<int> HandleAsync(OrderedCommand request, Func<OrderedCommand, CancellationToken, Task<int>> next, CancellationToken cancellationToken) {
            calls.Add(name);

            return await next(request, cancellationToken).ConfigureAwait(false);
        }
    }
}