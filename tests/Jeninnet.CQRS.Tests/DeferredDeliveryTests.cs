//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

[TestClass]
public class DeferredDeliveryTests {
    public TestContext? TestContext { get; set; }

    private static (IDeferredDispatcher Deferred, ManualTimeProvider Clock, IServiceProvider Provider, List<string> Calls) CreateHost() {
        var calls = new List<string>();
        var clock = new ManualTimeProvider();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(calls);
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddTransient<ICommandHandler<TestCommand, Unit>, RecordingHandler>();
        services.AddTransient<ICommandHandler<ResultCommand, string>, ResultRecordingHandler>();
        services.AddCqrsDeferredDelivery();
        var provider = services.BuildServiceProvider();

        return (provider.GetRequiredService<IDeferredDispatcher>(), clock, provider, calls);
    }

    private static async Task SpinUntilAsync(Func<bool> condition, string what, int timeoutMilliseconds = 2000) {
        var sw = Stopwatch.StartNew();

        while(!condition()) {
            if(sw.ElapsedMilliseconds > timeoutMilliseconds) {
                break;
            }

            await Task.Delay(10).ConfigureAwait(false);
        }

        MSTestAssertions.That(() => condition().Should().BeTrue($"timed out waiting for: {what}"));
    }

    [TestMethod]
    public async Task RunsAfterDue_InSubmissionOrderAsync() {
        // Arrange
        var (deferred, clock, _, calls) = CreateHost();
        await ((IHostedService)deferred).StartAsync(default);
        try {
            await deferred.DeferAsync(new TestCommand("B"), TimeSpan.FromMinutes(20), TestContext!.CancellationToken);
            await deferred.DeferAsync(new TestCommand("A"), TimeSpan.FromMinutes(10), TestContext!.CancellationToken);

            // Nothing runs while time stands still.
            await Task.Delay(100, TestContext!.CancellationToken).ConfigureAwait(false);
            MSTestAssertions.That(() => calls.Should().BeEmpty());

            // Act
            clock.Advance(TimeSpan.FromMinutes(30));
            await SpinUntilAsync(() => calls.Count == 2, "both deferred commands");

            // Assert: due order, not submission order.
            MSTestAssertions.That(() => calls.Should().Equal(["A", "B"]));
        }
        finally {
            await ((IHostedService)deferred).StopAsync(default).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task CancelledDefer_NeverRunsAsync() {
        // Arrange
        var (deferred, clock, _, calls) = CreateHost();
        await ((IHostedService)deferred).StartAsync(default);
        try {
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            // Act
            await deferred.DeferAsync(new TestCommand("X"), TimeSpan.FromMinutes(1), cts.Token);
            clock.Advance(TimeSpan.FromHours(1));
            await Task.Delay(200, TestContext!.CancellationToken).ConfigureAwait(false);

            // Assert
            MSTestAssertions.That(() => calls.Should().BeEmpty());
        }
        finally {
            await ((IHostedService)deferred).StopAsync(default).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task FaultedCommand_DoesNotStopPump_AndReportsAsync() {
        // Arrange
        var (deferred, clock, _, calls) = CreateHost();
        var faults = new List<DeferredFaultEventArgs>();
        deferred.Faulted += (_, args) => faults.Add(args);
        await ((IHostedService)deferred).StartAsync(default);
        try {
            // Act: a failing command followed by a good one.
            await deferred.DeferAsync(new TestCommand("boom"), TimeSpan.FromMinutes(1), TestContext!.CancellationToken);
            await deferred.DeferAsync(new TestCommand("ok"), TimeSpan.FromMinutes(2), TestContext!.CancellationToken);
            clock.Advance(TimeSpan.FromMinutes(5));
            await SpinUntilAsync(() => calls.Count == 1 && faults.Count == 1, "fault plus survivor");

            // Assert
            MSTestAssertions.That(() => calls.Should().Equal(["ok"]));
            MSTestAssertions.That(() => faults[0].CommandTypeName.Should().Contain(nameof(TestCommand)));
            MSTestAssertions.That(() => faults[0].Error.Should().BeOfType<InvalidOperationException>());
        }
        finally {
            await ((IHostedService)deferred).StopAsync(default).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task ImmediateDeferral_RunsPromptlyAsync() {
        // Arrange
        var (deferred, clock, _, calls) = CreateHost();
        await ((IHostedService)deferred).StartAsync(default);
        try {
            // Act
            await deferred.DeferAsync(new TestCommand("now"), TimeSpan.Zero, TestContext!.CancellationToken);
            await SpinUntilAsync(() => calls.Count == 1, "immediate command");

            // Assert
            MSTestAssertions.That(() => calls.Should().Equal(["now"]));
        }
        finally {
            await ((IHostedService)deferred).StopAsync(default).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task ResultCommand_Deferral_DiscardsResult_ButRunsHandlerAsync() {
        // Arrange
        var (deferred, clock, provider, _) = CreateHost();
        var calls = provider.GetRequiredService<List<string>>();
        await ((IHostedService)deferred).StartAsync(default);
        try {
            // Act
            await deferred.DeferAsync<ResultCommand, string>(new ResultCommand("ok"), TimeSpan.FromMinutes(1), TestContext!.CancellationToken);
            clock.Advance(TimeSpan.FromMinutes(2));
            await SpinUntilAsync(() => calls.Contains("result:ok"), "result command");

            // Assert: the handler ran; the value is fire-and-forget by contract.
            MSTestAssertions.That(() => calls.Should().Contain("result:ok"));
        }
        finally {
            await ((IHostedService)deferred).StopAsync(default).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public void Registration_SharesOneDispatcher_WithHosting() {
        // Arrange
        var services = new ServiceCollection();
        services.AddCqrsDeferredDelivery();
        var provider = services.BuildServiceProvider();

        // Act
        var dispatcher = provider.GetRequiredService<IDeferredDispatcher>();
        var hosted = provider.GetServices<IHostedService>().OfType<DeferredDispatcher>().Single();

        // Assert: the same instance serves both contracts.
        MSTestAssertions.That(() => ReferenceEquals(dispatcher, hosted).Should().BeTrue());
    }

    [TestMethod]
    public async Task RelaySubstitution_ReceivesEnvelope_HandlerNeverRunsAsync() {
        // Arrange: a recording relay registered first wins over the in-process default.
        var calls = new List<string>();
        var clock = new ManualTimeProvider();
        var received = new List<IDeferredEnvelope>();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(calls);
        services.AddSingleton<IMessageRelay>(new RecordingRelay(received));
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddTransient<ICommandHandler<TestCommand, Unit>, RecordingHandler>();
        services.AddCqrsDeferredDelivery();
        var provider = services.BuildServiceProvider();
        var deferred = provider.GetRequiredService<IDeferredDispatcher>();
        await ((IHostedService)deferred).StartAsync(TestContext!.CancellationToken);
        try {
            // Act
            await deferred.DeferAsync(new TestCommand("Hi"), TimeSpan.FromMinutes(1), TestContext!.CancellationToken);
            clock.Advance(TimeSpan.FromMinutes(2));
            await SpinUntilAsync(() => received.Count == 1, "relayed envelope");

            // Assert: the relay got the envelope; the handler never ran.
            MSTestAssertions.That(() => calls.Should().BeEmpty());
            MSTestAssertions.That(() => received.Single().Command.Should().BeOfType<TestCommand>());
        }
        finally {
            await ((IHostedService)deferred).StopAsync(default).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task Restart_ReplaysStagedEntry_ExactlyOnceAsync() {
        // Arrange: stage without starting the pump, then drop the first host.
        var calls = new List<string>();
        var clock = new ManualTimeProvider();
        var store = new InMemoryDeferredStore();
        ServiceProvider first;
        {
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton(calls);
            services.AddSingleton<IDeferredStore>(store);
            services.AddTransient<ICommandDispatcher, CommandDispatcher>();
            services.AddTransient<IQueryDispatcher, QueryDispatcher>();
            services.AddTransient<ICqrsBroker, CqrsBroker>();
            services.AddTransient<ICommandHandler<TestCommand, Unit>, RecordingHandler>();
            services.AddCqrsDeferredDelivery();
            first = services.BuildServiceProvider();
        }

        await first.GetRequiredService<IDeferredDispatcher>().DeferAsync(new TestCommand("Hi"), TimeSpan.FromMinutes(1), TestContext!.CancellationToken);
        await first.DisposeAsync();

        // Act: a fresh host sharing the store recovers the staged entry.
        var second = new ServiceCollection();
        second.AddSingleton<TimeProvider>(clock);
        second.AddSingleton(calls);
        second.AddSingleton<IDeferredStore>(store);
        second.AddTransient<ICommandDispatcher, CommandDispatcher>();
        second.AddTransient<IQueryDispatcher, QueryDispatcher>();
        second.AddTransient<ICqrsBroker, CqrsBroker>();
        second.AddTransient<ICommandHandler<TestCommand, Unit>, RecordingHandler>();
        second.AddCqrsDeferredDelivery();
        await using var provider = second.BuildServiceProvider();
        await ((IHostedService)provider.GetRequiredService<IDeferredDispatcher>()).StartAsync(TestContext!.CancellationToken);
        try {
            clock.Advance(TimeSpan.FromMinutes(2));
            await SpinUntilAsync(() => calls.Count == 1, "recovered command");

            // Assert: exactly once, and the store entry is completed.
            MSTestAssertions.That(() => calls.Should().Equal(["Hi"]));
            var listed = await store.ListAsync(TestContext!.CancellationToken);
            MSTestAssertions.That(() => listed.Should().BeEmpty());
        }
        finally {
            await ((IHostedService)provider.GetRequiredService<IDeferredDispatcher>()).StopAsync(default).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task CancelledDefer_CompletesStoreEntryAsync() {
        // Arrange
        var (deferred, clock, provider, _) = CreateHost();
        var store = provider.GetRequiredService<IDeferredStore>();
        await ((IHostedService)deferred).StartAsync(TestContext!.CancellationToken);
        try {
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            // Act
            await deferred.DeferAsync(new TestCommand("X"), TimeSpan.FromMinutes(1), cts.Token);
            clock.Advance(TimeSpan.FromHours(1));
            await Task.Delay(200, TestContext!.CancellationToken).ConfigureAwait(false);

            // Assert: skipped entries are completed, never replayed.
            var listed = await store.ListAsync(TestContext!.CancellationToken);
            MSTestAssertions.That(() => listed.Should().BeEmpty());
        }
        finally {
            await ((IHostedService)deferred).StopAsync(default).ConfigureAwait(false);
        }
    }

    private sealed class RecordingRelay(List<IDeferredEnvelope> received) : IMessageRelay {
        public Task RelayAsync(IDeferredEnvelope envelope, IServiceProvider services, CancellationToken cancellationToken) {
            received.Add(envelope);
            return Task.CompletedTask;
        }
    }

    private sealed record TestCommand(string Name) : ICommand;

    private sealed record ResultCommand(string Name) : ICommand<string>;

    private sealed class ResultRecordingHandler(List<string> calls) : ICommandHandler<ResultCommand, string> {
        public Task<string> HandleAsync(ResultCommand command, CancellationToken cancellationToken) {
            calls.Add($"result:{command.Name}");
            return Task.FromResult(command.Name);
        }
    }

    private sealed class RecordingHandler(List<string> calls) : ICommandHandler<TestCommand, Unit> {
        public Task<Unit> HandleAsync(TestCommand command, CancellationToken cancellationToken) {
            if(command.Name == "boom") {
                throw new InvalidOperationException("boom");
            }

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