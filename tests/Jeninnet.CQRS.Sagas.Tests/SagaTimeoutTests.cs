//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.Tests;

/// <summary>
/// Characterization test for CQRS-49 timeout scheduling: the transition persists a timeout command
/// through the deferred store, and advancing the fake clock moves the waiting saga to its timeout
/// state.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class SagaTimeoutTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task WaitingSaga_TimesOut_WhenTheFakeClockAdvancesAsync() {
        // Arrange
        var clock = new TestClock();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddCqrsDeferredDelivery();
        services.AddCqrsSagas<TimeoutSaga>();
        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToList();

        foreach(var service in hosted) {
            await service.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        }

        try {
            // Act: the initial transition schedules a persisted timeout.
            await provider.GetRequiredService<ICqrsBroker>()
                .PublishAsync(new TimeoutStarted("timeout-1"), TestContext!.CancellationToken).ConfigureAwait(false);
            var pending = await provider.GetRequiredService<IDeferredStore>()
                .ListAsync(TestContext!.CancellationToken).ConfigureAwait(false);

            // Assert: the command is persisted before it is due.
            MSTestAssertions.That(() => pending.Should().HaveCount(1));

            // Act: advance the fake clock; the deferred pump has a short real-time backstop.
            clock.Advance(TimeSpan.FromMinutes(5));
            var store = provider.GetRequiredService<ISagaStore>();
            await WaitUntilAsync(
                async () => {
                    var current = await store.LoadAsync("timeout-1", typeof(TimeoutSagaState), TestContext!.CancellationToken).ConfigureAwait(false);

                    return current?.State.CurrentState == "TimedOut";
                },
                TestContext!.CancellationToken).ConfigureAwait(false);
            var record = await store.LoadAsync("timeout-1", typeof(TimeoutSagaState), TestContext!.CancellationToken).ConfigureAwait(false);

            // Assert
            MSTestAssertions.That(() => record!.State.CurrentState.Should().Be("TimedOut"));
        }
        finally {
            foreach(var service in hosted.AsEnumerable().Reverse()) {
                await service.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken cancellationToken) {
        var deadline = Environment.TickCount64 + 10_000;

        while(!await condition().ConfigureAwait(false)) {
            if(Environment.TickCount64 > deadline) {
                throw new TimeoutException("timed out waiting for the saga to reach its timeout state.");
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }
}
