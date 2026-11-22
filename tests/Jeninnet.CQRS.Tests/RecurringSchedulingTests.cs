//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// Characterization tests for CQRS-38 recurring delivery: the schedule survives a host restart,
/// fires once per due instant on a fake clock, deduplicates by unique key, and the overlap gate
/// skips or queues as configured.
/// </summary>
[TestClass]
public class RecurringSchedulingTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task Schedule_SurvivesRestart_AndFiresOncePerDueMinuteAsync() {
        // Arrange
        var clock = new ManualTimeProvider();
        await using var first = CreateHost(clock);
        var cron = CronExpression.Parse("* * * * *");
        var options = new RecurringScheduleOptions { UniqueKey = "minute-tick" };
        var key = await first.Recurring.ScheduleAsync(new MinuteCommand("tick"), cron, options, TestContext!.CancellationToken);

        // Scheduling twice must not add a second occurrence.
        await first.Recurring.ScheduleAsync(new MinuteCommand("tick"), cron, options, TestContext!.CancellationToken);
        var pending = await first.Store.ListAsync(TestContext!.CancellationToken);

        MSTestAssertions.That(() => key.Should().Be("minute-tick"));
        MSTestAssertions.That(() => pending.Should().HaveCount(1));

        // Act 1: two due instants on the first host.
        await ((IHostedService)first.Dispatcher).StartAsync(TestContext!.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(1));
        await SpinUntilAsync(() => first.Calls.Count == 1, "first tick");
        clock.Advance(TimeSpan.FromMinutes(1));
        await SpinUntilAsync(() => first.Calls.Count == 2, "second tick");
        await ((IHostedService)first.Dispatcher).StopAsync(default);

        // Act 2: a fresh host over the same store resumes the series.
        await using var second = CreateHost(clock, first.Store, first.Calls);
        await ((IHostedService)second.Dispatcher).StartAsync(TestContext!.CancellationToken);

        try {
            clock.Advance(TimeSpan.FromMinutes(1));
            await SpinUntilAsync(() => first.Calls.Count == 3, "tick after restart");

            // Assert: exactly one execution per due instant, no duplicates.
            MSTestAssertions.That(() => first.Calls.Should().Equal(["tick", "tick", "tick"]));
        }
        finally {
            await ((IHostedService)second.Dispatcher).StopAsync(default);
        }
    }

    [TestMethod]
    public async Task Remove_CancelsThePendingOccurrenceAsync() {
        // Arrange
        var clock = new ManualTimeProvider();
        await using var host = CreateHost(clock);
        await host.Recurring.ScheduleAsync(new MinuteCommand("cleanup"), CronExpression.Parse("*/5 * * * *"), new RecurringScheduleOptions { UniqueKey = "cleanup" }, TestContext!.CancellationToken);

        // Act: remove before the pump recovers the entry.
        var removed = await host.Recurring.RemoveAsync("cleanup", TestContext!.CancellationToken);
        await ((IHostedService)host.Dispatcher).StartAsync(TestContext!.CancellationToken);

        try {
            clock.Advance(TimeSpan.FromMinutes(10));
            await Task.Delay(100, TestContext!.CancellationToken);

            // Assert
            MSTestAssertions.That(() => removed.Should().BeTrue());
            MSTestAssertions.That(() => host.Calls.Should().BeEmpty());
        }
        finally {
            await ((IHostedService)host.Dispatcher).StopAsync(default);
        }
    }

    [TestMethod]
    public async Task Misfire_RunsOnceThenResumesAsync() {
        // Arrange: schedule at 00:00, then let five occurrences pass while the host is down.
        var clock = new ManualTimeProvider();
        await using var host = CreateHost(clock);
        await host.Recurring.ScheduleAsync(new MinuteCommand("catchup"), CronExpression.Parse("* * * * *"), new RecurringScheduleOptions { UniqueKey = "catchup" }, TestContext!.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(5));

        // Act: recovery sees one overdue occurrence; it must run once, not five times.
        await ((IHostedService)host.Dispatcher).StartAsync(TestContext!.CancellationToken);

        try {
            await SpinUntilAsync(() => host.Calls.Count == 1, "one catch-up execution");
            clock.Advance(TimeSpan.FromMinutes(1));
            await SpinUntilAsync(() => host.Calls.Count == 2, "resumed schedule");

            // Assert
            MSTestAssertions.That(() => host.Calls.Should().Equal(["catchup", "catchup"]));
        }
        finally {
            await ((IHostedService)host.Dispatcher).StopAsync(default);
        }
    }

    [TestMethod]
    public async Task OverlapGate_SkipReturnsNull_AndQueueWaitsAsync() {
        // Arrange
        var gate = new RecurringExecutionGate();
        using var first = (await gate.EnterAsync("skip", RecurringOverlapPolicy.Skip, TestContext!.CancellationToken))!;

        // Act / Assert: skip is refused while busy and allowed after release.
        var skipped = await gate.EnterAsync("skip", RecurringOverlapPolicy.Skip, TestContext!.CancellationToken);
        MSTestAssertions.That(() => skipped.Should().BeNull());

        first.Dispose();
        using var afterRelease = (await gate.EnterAsync("skip", RecurringOverlapPolicy.Skip, TestContext!.CancellationToken))!;
        MSTestAssertions.That(() => afterRelease.Should().NotBeNull());

        // Queue: the second entrant waits until the first lease is disposed.
        using var holder = (await gate.EnterAsync("queue", RecurringOverlapPolicy.Queue, TestContext!.CancellationToken))!;
        var waiting = gate.EnterAsync("queue", RecurringOverlapPolicy.Queue, TestContext!.CancellationToken);
        await Task.Delay(50, TestContext!.CancellationToken);
        MSTestAssertions.That(() => waiting.IsCompleted.Should().BeFalse());

        holder.Dispose();
        using var queued = (await waiting.WaitAsync(TimeSpan.FromSeconds(5)))!;
        MSTestAssertions.That(() => queued.Should().NotBeNull());
    }

    private static Host CreateHost(ManualTimeProvider clock, InMemoryDeferredStore? store = null, List<string>? calls = null) {
        store ??= new InMemoryDeferredStore();
        calls ??= [];
        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton<IDeferredStore>(store);
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(calls);
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddTransient<ICommandHandler<MinuteCommand>, MinuteCommandHandler>();
        services.AddCqrsRecurringScheduling();
        var provider = services.BuildServiceProvider();

        return new Host(
            provider.GetRequiredService<IDeferredDispatcher>(),
            provider.GetRequiredService<IRecurringDispatcher>(),
            store,
            calls,
            provider);
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
        IRecurringDispatcher Recurring,
        InMemoryDeferredStore Store,
        List<string> Calls,
        ServiceProvider Provider) : IAsyncDisposable {
        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private sealed record MinuteCommand(string Name) : ICommand;

    private sealed class MinuteCommandHandler(List<string> calls) : ICommandHandler<MinuteCommand> {
        public Task<Unit> HandleAsync(MinuteCommand command, CancellationToken cancellationToken) {
            calls.Add(command.Name);

            return Task.FromResult(Unit.Value);
        }
    }

    private sealed class ManualTimeProvider : TimeProvider {
        private readonly Lock _sync = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() {
            lock(_sync) {
                return _now;
            }
        }

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void Advance(TimeSpan span) {
            List<ManualTimer> snapshot;
            lock(_sync) {
                _now += span;
                snapshot = [.. _timers];
            }

            foreach(var timer in snapshot) {
                timer.FireIfDue();
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) {
            var timer = new ManualTimer(callback, state, dueTime, period, this);
            lock(_sync) {
                _timers.Add(timer);
            }

            return timer;
        }

        private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period, ManualTimeProvider clock) : ITimer {
            private readonly TimerCallback _callback = callback;
            private readonly object? _state = state;
            private readonly ManualTimeProvider _clock = clock;
            private TimeSpan _due = dueTime;
            private TimeSpan _period = period;
            private DateTimeOffset _nextFire = clock.GetUtcNow() + (dueTime < TimeSpan.Zero ? Timeout.InfiniteTimeSpan : dueTime);
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period) {
                lock(_clock._sync) {
                    if(_disposed) {
                        return false;
                    }

                    _due = dueTime;
                    _period = period;
                    _nextFire = _clock._now + (dueTime < TimeSpan.Zero ? Timeout.InfiniteTimeSpan : dueTime);

                    return true;
                }
            }

            public void Dispose() {
                lock(_clock._sync) {
                    _disposed = true;
                    _clock._timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync() {
                Dispose();

                return ValueTask.CompletedTask;
            }

            public void FireIfDue() {
                TimerCallback? callback;
                object? state;

                lock(_clock._sync) {
                    if(_disposed || _due == Timeout.InfiniteTimeSpan || _clock._now < _nextFire) {
                        return;
                    }

                    if(_period == Timeout.InfiniteTimeSpan) {
                        _due = Timeout.InfiniteTimeSpan;
                    } else {
                        _nextFire += _period;
                    }

                    callback = _callback;
                    state = _state;
                }

                callback?.Invoke(state);
            }
        }
    }
}
