//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// Characterization tests for CQRS-25: dispatches that share a partition key serialize while
/// different keys run in parallel, and the partition map stays bounded without leaking permits.
/// </summary>
[TestClass]
public class PartitionedConcurrencyTests {
    private static (IPipelineBehavior<KeyedCommand, string> Behavior, PartitionedConcurrencyRegistry Registry) Build(PartitionedConcurrencyOptions options) {
        var services = new ServiceCollection();
        services.AddCqrsPartitionedConcurrency(options);

        var provider = services.BuildServiceProvider();

        return (provider.GetRequiredService<IPipelineBehavior<KeyedCommand, string>>(), provider.GetRequiredService<PartitionedConcurrencyRegistry>());
    }

    private static PartitionedConcurrencyOptions ByKey() =>
        new() {
            KeySelector = static request => ((KeyedCommand)request).Key,
        };

    [TestMethod]
    public async Task SameKey_SerializesAsync() {
        // Arrange: the first dispatch holds its key; the second reaches the behavior before release.
        var secondAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = ByKey();
        var selector = options.KeySelector!;
        options.KeySelector = request => {
            if(((KeyedCommand)request).Label is "second") {
                secondAttempted.TrySetResult();
            }

            return selector(request);
        };

        var (behavior, _) = Build(options);
        var tracker = new Tracker();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<string> Tracked(KeyedCommand command, CancellationToken cancellationToken) {
            tracker.Enter(command.Label);

            if(command.Label is "first") {
                firstEntered.TrySetResult();
            } else {
                secondEntered.TrySetResult();
            }

            await release.Task.ConfigureAwait(false);
            tracker.Exit(command.Label);

            return command.Key;
        }

        // Act
        var first = behavior.HandleAsync(new KeyedCommand("same", "first"), Tracked, default);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = behavior.HandleAsync(new KeyedCommand("same", "second"), Tracked, default);
        await secondAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert: the second dispatch has not entered while the first holds the key.
        MSTestAssertions.That(() => secondEntered.Task.IsCompleted.Should().BeFalse());

        release.TrySetResult();
        await Task.WhenAll(first, second);

        MSTestAssertions.That(() => tracker.MaxActive.Should().Be(1));
        MSTestAssertions.That(() => tracker.Joined.Should().Be("first-enter,first-exit,second-enter,second-exit"));
    }

    [TestMethod]
    public async Task DifferentKeys_RunInParallelAsync() {
        // Arrange
        var (behavior, _) = Build(ByKey());
        var tracker = new Tracker();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<string> Tracked(KeyedCommand command, CancellationToken cancellationToken) {
            tracker.Enter(command.Label);

            if(command.Label is "first") {
                firstEntered.TrySetResult();
            } else {
                secondEntered.TrySetResult();
            }

            await release.Task.ConfigureAwait(false);
            tracker.Exit(command.Label);

            return command.Key;
        }

        // Act: the second key must enter while the first is still blocked.
        var first = behavior.HandleAsync(new KeyedCommand("a", "first"), Tracked, default);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = behavior.HandleAsync(new KeyedCommand("b", "second"), Tracked, default);
        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        MSTestAssertions.That(() => tracker.Active.Should().Be(2));

        release.TrySetResult();
        await Task.WhenAll(first, second);

        MSTestAssertions.That(() => tracker.MaxActive.Should().Be(2));
    }

    [TestMethod]
    public async Task FailFastWhenBusy_ThrowsAndReleasesNothingAsync() {
        // Arrange
        var options = ByKey();
        options.FailFastWhenBusy = true;
        var (behavior, _) = Build(options);
        var tracker = new Tracker();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<string> Tracked(KeyedCommand command, CancellationToken cancellationToken) {
            tracker.Enter(command.Label);
            entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
            tracker.Exit(command.Label);

            return command.Key;
        }

        var holding = behavior.HandleAsync(new KeyedCommand("same", "holder"), Tracked, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        var thrown = await CaptureAsync(() => behavior.HandleAsync(new KeyedCommand("same", "rejected"), Tracked, default));

        // Assert: rejected while the key is busy, handler never ran.
        MSTestAssertions.That(() => thrown.Should().BeOfType<PartitionBusyException>());
        MSTestAssertions.That(() => ((PartitionBusyException)thrown!).Key.Should().Be("same"));
        MSTestAssertions.That(() => tracker.Joined.Should().Be("holder-enter"));

        release.TrySetResult();
        await holding;

        // The rejection did not leak a permit: the next dispatch flows.
        var after = await behavior.HandleAsync(new KeyedCommand("same", "after"), Tracked, default).WaitAsync(TimeSpan.FromSeconds(5));
        MSTestAssertions.That(() => after.Should().Be("same"));
    }

    [TestMethod]
    public async Task DefaultPartitionKey_IsTheFullyQualifiedRequestTypeAsync() {
        // Arrange: no selector, so the behavior's cached default key is used.
        var options = new PartitionedConcurrencyOptions { FailFastWhenBusy = true };
        var (behavior, _) = Build(options);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<string> Blocking(KeyedCommand command, CancellationToken cancellationToken) {
            entered.TrySetResult();
            await release.Task.ConfigureAwait(false);

            return command.Key;
        }

        var holding = behavior.HandleAsync(new KeyedCommand("same", "holder"), Blocking, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        var thrown = await CaptureAsync(() => behavior.HandleAsync(new KeyedCommand("same", "rejected"), Blocking, default));

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<PartitionBusyException>());
        MSTestAssertions.That(() => ((PartitionBusyException)thrown!).Key.Should().Be(typeof(KeyedCommand).FullName));

        release.TrySetResult();
        await holding;
    }

    [TestMethod]
    public async Task CancellationWhileWaiting_DoesNotLeakPermitAsync() {
        // Arrange: one dispatch holds the key; a queued dispatch is cancelled before it acquires.
        var (behavior, _) = Build(ByKey());
        var tracker = new Tracker();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<string> Tracked(KeyedCommand command, CancellationToken cancellationToken) {
            tracker.Enter(command.Label);

            if(command.Label is "holder") {
                entered.TrySetResult();
            }

            await release.Task.ConfigureAwait(false);
            tracker.Exit(command.Label);

            return command.Key;
        }

        var holding = behavior.HandleAsync(new KeyedCommand("same", "holder"), Tracked, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var thrown = await CaptureAsync(() => behavior.HandleAsync(new KeyedCommand("same", "cancelled"), Tracked, cts.Token));

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeAssignableTo<OperationCanceledException>());
        MSTestAssertions.That(() => tracker.Joined.Should().Be("holder-enter"));

        release.TrySetResult();
        await holding;

        // Only the acquired permit was released; a later dispatch still enters.
        var after = await behavior.HandleAsync(new KeyedCommand("same", "after"), Tracked, default).WaitAsync(TimeSpan.FromSeconds(5));
        MSTestAssertions.That(() => after.Should().Be("same"));
    }

    [TestMethod]
    public async Task ConcurrencyLimitAboveOne_AdmitsUpToLimitAsync() {
        // Arrange: two permits for the key, a third dispatch must wait.
        var options = ByKey();
        options.ConcurrencyLimit = 2;
        var thirdAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var selector = options.KeySelector!;
        options.KeySelector = request => {
            if(((KeyedCommand)request).Label is "third") {
                thirdAttempted.TrySetResult();
            }

            return selector(request);
        };

        var (behavior, _) = Build(options);
        var tracker = new Tracker();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thirdEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var admitted = 0;

        async Task<string> Tracked(KeyedCommand command, CancellationToken cancellationToken) {
            tracker.Enter(command.Label);

            if(Interlocked.Increment(ref admitted) <= 2) {
                entered.TrySetResult();
            } else {
                thirdEntered.TrySetResult();
            }

            await release.Task.ConfigureAwait(false);
            tracker.Exit(command.Label);

            return command.Key;
        }

        // Act
        var first = behavior.HandleAsync(new KeyedCommand("same", "first"), Tracked, default);
        var second = behavior.HandleAsync(new KeyedCommand("same", "second"), Tracked, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => tracker.MaxActive == 2);

        var third = behavior.HandleAsync(new KeyedCommand("same", "third"), Tracked, default);
        await thirdAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert: the third waits while two run.
        MSTestAssertions.That(() => thirdEntered.Task.IsCompleted.Should().BeFalse());
        MSTestAssertions.That(() => tracker.Active.Should().Be(2));

        release.TrySetResult();
        await Task.WhenAll(first, second, third);

        MSTestAssertions.That(() => tracker.MaxActive.Should().Be(2));
    }

    [TestMethod]
    public async Task IdlePartitions_AreEvictedAboveBoundAsync() {
        // Arrange
        var registry = new PartitionedConcurrencyRegistry(Options.Create(new PartitionedConcurrencyOptions { MaxPartitions = 2 }));

        // Act: touch three keys one after another.
        foreach(var key in new[] { "a", "b", "c" }) {
            using var lease = await registry.AcquireAsync(key, default);
        }

        // Assert: the most recent partition survives and the map is bounded.
        MSTestAssertions.That(() => registry.Count.Should().Be(2));
        MSTestAssertions.That(() => registry.ContainsKey("c").Should().BeTrue());
    }

    [TestMethod]
    public async Task ActivePartition_IsNeverEvictedAsync() {
        // Arrange: the bound is 1 and one key is held.
        var registry = new PartitionedConcurrencyRegistry(Options.Create(new PartitionedConcurrencyOptions { MaxPartitions = 1 }));
        using var hot = await registry.AcquireAsync("hot", default);

        // Act: an idle partition is created and released while "hot" is active.
        using(var cold = await registry.AcquireAsync("cold", default)) {
            MSTestAssertions.That(() => registry.Count.Should().Be(2));
        }

        // Assert: the busy partition survives; only the idle one is evicted.
        MSTestAssertions.That(() => registry.ContainsKey("hot").Should().BeTrue());
        MSTestAssertions.That(() => registry.Count.Should().Be(1));
    }

    [TestMethod]
    public void Registration_SharesOneRegistry() {
        // Arrange
        var services = new ServiceCollection();
        services.AddCqrsPartitionedConcurrency(new PartitionedConcurrencyOptions());
        var provider = services.BuildServiceProvider();

        // Act
        var first = provider.GetRequiredService<PartitionedConcurrencyRegistry>();
        var second = provider.GetRequiredService<PartitionedConcurrencyRegistry>();
        var behaviors = provider.GetServices<IPipelineBehavior<KeyedCommand, string>>().ToList();

        // Assert: one shared registry, transient behaviors.
        MSTestAssertions.That(() => ReferenceEquals(first, second).Should().BeTrue());
        MSTestAssertions.That(() => behaviors.Should().NotBeEmpty());
    }

    private static async Task WaitUntilAsync(Func<bool> condition) {
        var deadline = Environment.TickCount64 + 5000;

        while(!condition()) {
            if(Environment.TickCount64 > deadline) {
                throw new TimeoutException("Condition was not reached in time.");
            }

            await Task.Yield();
        }
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

    private sealed record KeyedCommand(string Key, string Label) : ICommand<string>;

    private sealed class Tracker {
        private readonly object _gate = new();
        private readonly List<string> _log = [];
        private int _active;

        internal int Active {
            get {
                lock(_gate) {
                    return _active;
                }
            }
        }

        internal int MaxActive { get; private set; }

        internal string Joined {
            get {
                lock(_gate) {
                    return string.Join(",", _log);
                }
            }
        }

        internal void Enter(string label) {
            lock(_gate) {
                _active++;
                MaxActive = Math.Max(MaxActive, _active);
                _log.Add($"{label}-enter");
            }
        }

        internal void Exit(string label) {
            lock(_gate) {
                _log.Add($"{label}-exit");
                _active--;
            }
        }
    }
}
