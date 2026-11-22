//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// Characterization tests for CQRS-37 graceful drain: shutdown stops accepting work and lets the
/// in-flight deferred command finish, while the rest stay staged; a drain timeout cancels the
/// handler bounded by the deadline and leaves the entry for the next start (at-least-once).
/// </summary>
[TestClass]
public class DeferredDrainTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task Stop_LetsTheInFlightCommandFinish_AndLeavesTheRestStagedAsync() {
        // Arrange
        await using var host = CreateHost();
        await ((IHostedService)host.Dispatcher).StartAsync(TestContext!.CancellationToken);
        await host.Dispatcher.DeferAsync(new DrainCommand("slow"), TimeSpan.Zero, TestContext!.CancellationToken);
        await host.Dispatcher.DeferAsync(new DrainCommand("next"), TimeSpan.Zero, TestContext!.CancellationToken);
        await host.State.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act: shutdown begins while "slow" is running, then the handler releases.
        var stop = ((IHostedService)host.Dispatcher).StopAsync(TestContext!.CancellationToken);
        host.State.Release.TrySetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert: the in-flight item completed and was completed; the next item stayed staged.
        MSTestAssertions.That(() => host.Calls.Should().Equal(["slow"]));
        var pending = await host.Store.ListAsync(TestContext!.CancellationToken);
        MSTestAssertions.That(() => pending.Should().HaveCount(1));

        // Act 2: the next start replays only the remaining item.
        var restarted = CreateHost(host.Store, host.Calls, host.State);
        await ((IHostedService)restarted.Dispatcher).StartAsync(TestContext!.CancellationToken);

        try {
            await SpinUntilAsync(() => host.Calls.Count == 2, "replay of the remaining command");
            MSTestAssertions.That(() => host.Calls.Should().Equal(["slow", "next"]));
        }
        finally {
            await ((IHostedService)restarted.Dispatcher).StopAsync(default).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task Stop_WithShortDrainTimeout_ReturnsPromptly_AndTheEntryReplaysAsync() {
        // Arrange
        await using var host = CreateHost(drainTimeout: TimeSpan.FromMilliseconds(100));
        await ((IHostedService)host.Dispatcher).StartAsync(TestContext!.CancellationToken);
        await host.Dispatcher.DeferAsync(new DrainCommand("slow"), TimeSpan.Zero, TestContext!.CancellationToken);
        await host.State.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act: the handler ignores cancellation and stays blocked.
        var stopwatch = Stopwatch.StartNew();
        await ((IHostedService)host.Dispatcher).StopAsync(TestContext!.CancellationToken).WaitAsync(TimeSpan.FromSeconds(2));
        stopwatch.Stop();
        host.State.Release.TrySetResult();

        // Assert: shutdown respected the deadline and the interrupted entry is still staged.
        MSTestAssertions.That(() => stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2)));
        var pending = await host.Store.ListAsync(TestContext!.CancellationToken);
        MSTestAssertions.That(() => pending.Should().HaveCount(1));

        // Act 2: a fresh host replays it (at-least-once).
        await Task.Delay(100, TestContext!.CancellationToken);
        var restarted = CreateHost(host.Store, host.Calls, host.State);
        await ((IHostedService)restarted.Dispatcher).StartAsync(TestContext!.CancellationToken);

        try {
            await SpinUntilAsync(() => host.Calls.Count == 2, "replay after drain timeout");
            MSTestAssertions.That(() => host.Calls.Should().Equal(["slow", "slow"]));
        }
        finally {
            await ((IHostedService)restarted.Dispatcher).StopAsync(default).ConfigureAwait(false);
        }
    }

    private static Host CreateHost(
        InMemoryDeferredStore? store = null,
        List<string>? calls = null,
        DrainState? state = null,
        TimeSpan? drainTimeout = null) {
        store ??= new InMemoryDeferredStore();
        calls ??= [];
        state ??= new DrainState();
        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton<IDeferredStore>(store);
        services.AddSingleton(calls);
        services.AddSingleton(state);
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddTransient<ICommandHandler<DrainCommand>, DrainCommandHandler>();
        services.AddCqrsDeferredDelivery();
        services.Configure<DeferredDeliveryOptions>(options => options.DrainTimeout = drainTimeout ?? TimeSpan.FromSeconds(10));
        var provider = services.BuildServiceProvider();

        return new Host(provider.GetRequiredService<IDeferredDispatcher>(), store, calls, state, provider);
    }

    private static async Task SpinUntilAsync(Func<bool> condition, string what) {
        var deadline = Environment.TickCount64 + 5000;

        while(!condition()) {
            if(Environment.TickCount64 > deadline) {
                throw new TimeoutException($"timed out waiting for: {what}");
            }

            await Task.Yield();
        }
    }

    private sealed record Host(
        IDeferredDispatcher Dispatcher,
        InMemoryDeferredStore Store,
        List<string> Calls,
        DrainState State,
        ServiceProvider Provider) : IAsyncDisposable {
        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private sealed class DrainState {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record DrainCommand(string Name) : ICommand;

    private sealed class DrainCommandHandler(List<string> calls, DrainState state) : ICommandHandler<DrainCommand> {
        public async Task<Unit> HandleAsync(DrainCommand command, CancellationToken cancellationToken) {
            state.Entered.TrySetResult();

            if(command.Name is "slow") {
                await state.Release.Task.ConfigureAwait(false);
            }

            calls.Add(command.Name);

            return Unit.Value;
        }
    }
}