//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// Characterization tests for CQRS-26: a failing deferred command lands in the dead-letter
/// store with its attempt count and error, replay re-stages it, success removes it, failure
/// increments attempts, and retention bounds the store.
/// </summary>
[TestClass]
public class DeadLetterTests {
    public TestContext? TestContext { get; set; }

    private static (IDeferredDispatcher Deferred, IDeadLetterStore DeadLetters, List<string> Calls, FailureSwitch Switch) CreateHost() {
        var calls = new List<string>();
        var failureSwitch = new FailureSwitch();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddSingleton(failureSwitch);
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddTransient<ICommandHandler<DeadLetterPing>, DeadLetterPingHandler>();
        services.AddCqrsDeferredDelivery();
        var provider = services.BuildServiceProvider();

        return (provider.GetRequiredService<IDeferredDispatcher>(), provider.GetRequiredService<IDeadLetterStore>(), calls, failureSwitch);
    }

    private static async Task SpinUntilAsync(Func<Task<bool>> condition, string what, int timeoutMilliseconds = 5000) {
        var sw = Stopwatch.StartNew();

        while(!await condition().ConfigureAwait(false)) {
            if(sw.ElapsedMilliseconds > timeoutMilliseconds) {
                throw new TimeoutException($"timed out waiting for: {what}");
            }

            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task FailingDeferredCommand_LandsInDeadLetterAsync() {
        // Arrange
        var (deferred, deadLetters, calls, _) = CreateHost();
        await ((IHostedService)deferred).StartAsync(TestContext!.CancellationToken);

        try {
            // Act
            await deferred.DeferAsync(new DeadLetterPing("boom"), TimeSpan.Zero, TestContext!.CancellationToken);
            await SpinUntilAsync(async () => (await deadLetters.ListAsync(TestContext!.CancellationToken).ConfigureAwait(false)).Count == 1, "dead letter recorded");

            // Assert: attempts, error, and the original command are all captured.
            var entry = (await deadLetters.ListAsync(TestContext!.CancellationToken).ConfigureAwait(false)).Single();
            MSTestAssertions.That(() => calls.Should().BeEmpty());
            MSTestAssertions.That(() => entry.Attempts.Should().Be(1));
            MSTestAssertions.That(() => entry.LastErrorType.Should().Contain(nameof(InvalidOperationException)));
            MSTestAssertions.That(() => entry.LastErrorMessage.Should().Be("boom"));
            MSTestAssertions.That(() => entry.CommandType.Should().Contain(nameof(DeadLetterPing)));
        }
        finally {
            await ((IHostedService)deferred).StopAsync(default).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task Replay_Success_RemovesFromDeadLetterAsync() {
        // Arrange: fail once, then let the replay succeed.
        var (deferred, deadLetters, calls, failureSwitch) = CreateHost();
        await ((IHostedService)deferred).StartAsync(TestContext!.CancellationToken);

        try {
            await deferred.DeferAsync(new DeadLetterPing("retry-me"), TimeSpan.Zero, TestContext!.CancellationToken);
            await SpinUntilAsync(async () => (await deadLetters.ListAsync(TestContext!.CancellationToken).ConfigureAwait(false)).Count == 1, "dead letter recorded");
            var entry = (await deadLetters.ListAsync(TestContext!.CancellationToken).ConfigureAwait(false)).Single();

            // Act
            failureSwitch.Fail = false;
            var replayed = await deferred.ReplayAsync(entry.Key, TestContext!.CancellationToken);

            // Assert
            MSTestAssertions.That(() => replayed.Should().BeTrue());
            await SpinUntilAsync(() => Task.FromResult(calls.Count == 1), "replayed command");
            await SpinUntilAsync(async () => (await deadLetters.ListAsync(TestContext!.CancellationToken).ConfigureAwait(false)).Count == 0, "dead letter removed");
            MSTestAssertions.That(() => calls.Should().Equal(["retry-me"]));
        }
        finally {
            await ((IHostedService)deferred).StopAsync(default).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task Replay_Failure_IncrementsAttemptsAsync() {
        // Arrange: the command always fails.
        var (deferred, deadLetters, _, _) = CreateHost();
        await ((IHostedService)deferred).StartAsync(TestContext!.CancellationToken);

        try {
            await deferred.DeferAsync(new DeadLetterPing("poison"), TimeSpan.Zero, TestContext!.CancellationToken);
            await SpinUntilAsync(async () => (await deadLetters.ListAsync(TestContext!.CancellationToken).ConfigureAwait(false)).Count == 1, "dead letter recorded");
            var entry = (await deadLetters.ListAsync(TestContext!.CancellationToken).ConfigureAwait(false)).Single();

            // Act
            var replayed = await deferred.ReplayAsync(entry.Key, TestContext!.CancellationToken);

            // Assert: the entry stays and the attempt count advances.
            MSTestAssertions.That(() => replayed.Should().BeTrue());
            await SpinUntilAsync(async () => (await deadLetters.GetAsync(entry.Key, TestContext!.CancellationToken).ConfigureAwait(false))?.Attempts == 2, "attempt increment");
            var updated = await deadLetters.GetAsync(entry.Key, TestContext!.CancellationToken);
            MSTestAssertions.That(() => updated!.LastErrorMessage.Should().Be("boom"));
        }
        finally {
            await ((IHostedService)deferred).StopAsync(default).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task UnknownReplayKey_ReturnsFalseAsync() {
        // Arrange
        var (deferred, _, _, _) = CreateHost();

        // Act
        var replayed = await deferred.ReplayAsync("missing", TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => replayed.Should().BeFalse());
    }

    [TestMethod]
    public async Task RetentionAndMaxEntries_BoundTheStoreAsync() {
        // Arrange: three failures, a two-entry cap, and a one-hour retention.
        var options = Options.Create(new DeadLetterOptions {
            MaxEntries = 2,
            Retention = TimeSpan.FromHours(1),
        });
        var store = new InMemoryDeadLetterStore(options);
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var envelope = new DeferredEnvelope<DeadLetterPing>(new DeadLetterPing("x"), now, null);

        await store.RecordAsync(envelope, new InvalidOperationException("oldest"), now.AddHours(-3), TestContext!.CancellationToken);
        await store.RecordAsync(envelope, new InvalidOperationException("older"), now.AddHours(-2), TestContext!.CancellationToken);
        await store.RecordAsync(envelope, new InvalidOperationException("recent"), now.AddHours(-1), TestContext!.CancellationToken);

        // Act: the cap evicted the oldest before pruning ran.
        var afterInsert = await store.ListAsync(TestContext!.CancellationToken);
        var pruned = await store.PruneAsync(now, TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => afterInsert.Should().HaveCount(2));
        MSTestAssertions.That(() => afterInsert.Any(entry => entry.LastErrorMessage == "oldest").Should().BeFalse());
        MSTestAssertions.That(() => pruned.Should().Be(1));
        MSTestAssertions.That(() => (store.ListAsync(TestContext!.CancellationToken).GetAwaiter().GetResult()).Should().HaveCount(1));
    }

    [TestMethod]
    public async Task HostedPruning_RemovesExpiredEntriesAsync() {
        // Arrange: a short interval and an entry already outside retention.
        var services = new ServiceCollection();
        services.AddCqrsDeadLetters(options => {
            options.Retention = TimeSpan.FromHours(1);
            options.PruneInterval = TimeSpan.FromMilliseconds(25);
        });
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var envelope = new DeferredEnvelope<DeadLetterPing>(new DeadLetterPing("stale"), DateTimeOffset.UtcNow, null);
        await store.RecordAsync(envelope, new InvalidOperationException("stale"), DateTimeOffset.UtcNow.AddHours(-2), TestContext!.CancellationToken);
        var pruning = provider.GetServices<IHostedService>().OfType<DeadLetterPruningService>().Single();

        // Act
        await pruning.StartAsync(TestContext!.CancellationToken);

        try {
            await SpinUntilAsync(async () => (await store.ListAsync(TestContext!.CancellationToken).ConfigureAwait(false)).Count == 0, "hosted pruning");
        }
        finally {
            await pruning.StopAsync(default).ConfigureAwait(false);
        }
    }

    private sealed class FailureSwitch {
        public bool Fail { get; set; } = true;
    }

    private sealed record DeadLetterPing(string Name) : ICommand;

    private sealed class DeadLetterPingHandler(List<string> calls, FailureSwitch failureSwitch) : ICommandHandler<DeadLetterPing> {
        public Task<Unit> HandleAsync(DeadLetterPing command, CancellationToken cancellationToken) {
            if(failureSwitch.Fail) {
                throw new InvalidOperationException("boom");
            }

            calls.Add(command.Name);

            return Task.FromResult(Unit.Value);
        }
    }
}