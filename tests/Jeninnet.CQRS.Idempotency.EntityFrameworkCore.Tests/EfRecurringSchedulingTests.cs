//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore.Tests;

/// <summary>
/// Characterization test for CQRS-38 on EF Core: a recurring occurrence and its cron metadata
/// survive a host restart, and the first due instant fires exactly once.
/// </summary>
[TestClass]
public class EfRecurringSchedulingTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task Schedule_SurvivesRestart_AndFiresOnceAsync() {
        // Arrange: schedule without starting the pump, then drop the host.
        var file = Path.Combine(Path.GetTempPath(), $"cqrs38-{Guid.NewGuid():N}.db");
        var clock = new ManualTimeProvider();
        var calls = new List<string>();

        await using(var first = BuildHost(file, clock, calls)) {
            var recurring = first.GetRequiredService<IRecurringDispatcher>();
            await recurring.ScheduleAsync(
                new MinuteCommand("tick"),
                CronExpression.Parse("* * * * *"),
                new RecurringScheduleOptions { UniqueKey = "ef-minute" },
                TestContext!.CancellationToken);
        }

        // Act: a fresh host over the same file recovers and fires the due occurrence.
        await using var second = BuildHost(file, clock, calls);
        var dispatcher = second.GetRequiredService<IDeferredDispatcher>();
        await ((IHostedService)dispatcher).StartAsync(TestContext!.CancellationToken);

        try {
            clock.Advance(TimeSpan.FromMinutes(1));
            await SpinUntilAsync(() => calls.Count == 1, "recovered recurring occurrence");
            await Task.Delay(150, TestContext!.CancellationToken);

            // Assert: one execution, and the next occurrence has been staged.
            MSTestAssertions.That(() => calls.Should().Equal(["tick"]));
            var pending = await second.GetRequiredService<IDeferredStore>().ListAsync(TestContext!.CancellationToken);
            MSTestAssertions.That(() => pending.Should().HaveCount(1));
        }
        finally {
            await ((IHostedService)dispatcher).StopAsync(default).ConfigureAwait(false);
        }
    }

    private static ServiceProvider BuildHost(string file, ManualTimeProvider clock, List<string> calls) {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(calls);
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddTransient<ICommandHandler<MinuteCommand>, MinuteCommandHandler>();
        services.AddCqrsEfDeferredDelivery(builder => builder.UseSqlite($"Data Source={file}"));
        services.AddCqrsRecurringScheduling();
        var provider = services.BuildServiceProvider();

        using(var scope = provider.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>().CreateDbContext();
            db.Database.EnsureCreated();
        }

        return provider;
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