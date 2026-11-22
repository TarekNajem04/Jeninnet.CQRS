//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.Tests;

/// <summary>
/// CQRS-58 routing slips: ordered steps, reverse compensation, retry/timeout policy, compensation
/// dead-lettering, and definition validation. The file-backed restart test lives in the EF suite.
/// </summary>
[TestClass]
public sealed class RoutingSlipTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task ThreeStepSlip_CompensatesTheFirstStep_AndNeverRunsTheThirdAsync() {
        // Arrange
        var log = new SlipLog();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddRoutingSlip<SlipArguments>(
            slip => slip
                .AddStep<ReserveActivity>("reserve")
                .AddStep<FailActivity>("charge")
                .AddStep<NeverActivity>("ship"),
            "three-step");
        await using var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<RoutingSlipExecutor>();
        var store = provider.GetRequiredService<IRoutingSlipStore>();

        // Act
        var result = await executor.ExecuteAsync(new SlipArguments(), "slip-1", TestContext!.CancellationToken).ConfigureAwait(false);
        var record = await store.LoadAsync("slip-1", TestContext!.CancellationToken).ConfigureAwait(false);
        var snapshots = await store.ListAsync("three-step", TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => result.Status.Should().Be(RoutingSlipStatus.Compensated));
        MSTestAssertions.That(() => result.CompletedStepCount.Should().Be(1));
        MSTestAssertions.That(() => result.CompensatedStepCount.Should().Be(1));
        MSTestAssertions.That(() => result.FailedStep.Should().Be("charge"));
        MSTestAssertions.That(() => result.Error.Should().BeOfType<InvalidOperationException>());
        MSTestAssertions.That(() => log.Entries.Should().Equal(["reserve", "fail", "undo:r-1"]));
        MSTestAssertions.That(() => record.Should().NotBeNull());
        MSTestAssertions.That(() => record!.Status.Should().Be(RoutingSlipStatus.Compensated));
        MSTestAssertions.That(() => record!.CompletedSteps.Should().Equal(["reserve"]));
        MSTestAssertions.That(() => record!.CompensatedSteps.Should().Equal(["reserve"]));
        MSTestAssertions.That(() => snapshots.Should().HaveCount(1));
        MSTestAssertions.That(() => snapshots[0].Status.Should().Be(RoutingSlipStatus.Compensated));
    }

    [TestMethod]
    public async Task CompensationFailure_DeadLettersBothErrorsAsync() {
        // Arrange
        var log = new SlipLog();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddRoutingSlip<SlipArguments>(
            slip => slip
                .AddStep<ReserveFailCompensationActivity>("reserve")
                .AddStep<FailActivity>("charge"),
            "failing-compensation");
        await using var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<RoutingSlipExecutor>();
        var deadLetters = provider.GetRequiredService<IDeadLetterStore>();

        // Act
        var result = await executor.ExecuteAsync(new SlipArguments(), "slip-1", TestContext!.CancellationToken).ConfigureAwait(false);
        var entries = await deadLetters.ListAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => result.Status.Should().Be(RoutingSlipStatus.Faulted));
        MSTestAssertions.That(() => result.Error.Should().BeOfType<InvalidOperationException>());
        MSTestAssertions.That(() => result.Error!.Message.Should().Contain("step-2-failed"));
        MSTestAssertions.That(() => result.CompensationError.Should().BeOfType<RoutingSlipCompensationException>());
        MSTestAssertions.That(() => result.CompensationError!.Message.Should().Contain("step-2-failed"));
        MSTestAssertions.That(() => result.CompensationError!.Message.Should().Contain("undo-failed"));
        MSTestAssertions.That(() => log.Entries.Should().Equal(["reserve", "fail", "undo:reserve"]));
        MSTestAssertions.That(() => entries.Should().HaveCount(1));
        MSTestAssertions.That(() => entries[0].LastErrorMessage.Should().Contain("step-2-failed"));
        MSTestAssertions.That(() => entries[0].LastErrorMessage.Should().Contain("undo-failed"));

        var info = entries[0].Envelope.Command as RoutingSlipDeadLetterInfo;
        MSTestAssertions.That(() => info.Should().NotBeNull());
        MSTestAssertions.That(() => info!.FailureErrorMessage.Should().Contain("step-2-failed"));
        MSTestAssertions.That(() => info!.CompensationErrorMessage.Should().Contain("undo-failed"));
    }

    [TestMethod]
    public async Task RetryPolicy_RetriesTheStepBeforeContinuingAsync() {
        // Arrange
        var log = new SlipLog();
        var attempts = new AttemptState();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddSingleton(attempts);
        services.AddRoutingSlip<SlipArguments>(
            slip => slip.AddStep<FlakyActivity>(options => {
                options.MaxAttempts = 3;
                options.RetryDelay = TimeSpan.FromMilliseconds(1);
            }, "flaky"),
            "retry");
        await using var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<RoutingSlipExecutor>();

        // Act
        var result = await executor.ExecuteAsync(new SlipArguments(), "slip-1", TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => result.Status.Should().Be(RoutingSlipStatus.Completed));
        MSTestAssertions.That(() => attempts.Attempts.Should().Be(3));
        MSTestAssertions.That(() => log.Entries.Should().Equal(["attempt:1", "attempt:2", "attempt:3"]));
    }

    [TestMethod]
    public async Task Timeout_CancelsTheStepAndCompensatesAsync() {
        // Arrange
        var services = new ServiceCollection();
        var log = new SlipLog();
        services.AddSingleton(log);
        services.AddRoutingSlip<SlipArguments>(
            slip => slip.AddStep<SlowActivity>(options => {
                options.Timeout = TimeSpan.FromMilliseconds(50);
            }, "slow"),
            "timeout");
        await using var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<RoutingSlipExecutor>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var result = await executor.ExecuteAsync(new SlipArguments(), "slip-1", TestContext!.CancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        // Assert
        MSTestAssertions.That(() => result.Status.Should().Be(RoutingSlipStatus.Compensated));
        MSTestAssertions.That(() => result.Error.Should().BeOfType<TimeoutException>());
        MSTestAssertions.That(() => result.Error!.Message.Should().Contain("slow"));
        MSTestAssertions.That(() => stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5)));
    }

    [TestMethod]
    public void DuplicateDefinitionName_FailsWhenTheRegistryIsResolved() {
        // Arrange
        var services = new ServiceCollection();
        services.AddRoutingSlip<SlipArguments>(slip => slip.AddStep<ReserveActivity>(), "duplicate");
        services.AddRoutingSlip<SlipArguments>(slip => slip.AddStep<NeverActivity>(), "duplicate");
        using var provider = services.BuildServiceProvider();

        // Act
        Exception? thrown = null;

        try {
            _ = provider.GetRequiredService<RoutingSlipDefinitionRegistry>();
        }
        catch(Exception exception) {
            thrown = exception;
        }

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<InvalidOperationException>());
        MSTestAssertions.That(() => thrown!.Message.Should().Contain("duplicate"));
    }

    public sealed class SlipLog {
        private readonly List<string> _entries = [];

        public IReadOnlyList<string> Entries => _entries;

        public void Add(string entry) => _entries.Add(entry);
    }

    public sealed class AttemptState {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        public int Next() => Interlocked.Increment(ref _attempts);
    }

    public sealed class SlipArguments {
        public string? Reservation { get; set; }
    }

    public sealed class ReserveActivity(SlipLog log) : IActivity<SlipArguments> {
        private readonly SlipLog _log = log ?? throw new ArgumentNullException(nameof(log));

        public Task ExecuteAsync(SlipArguments arguments, CancellationToken cancellationToken) {
            _log.Add("reserve");
            arguments.Reservation = "r-1";

            return Task.CompletedTask;
        }

        public Task CompensateAsync(SlipArguments arguments, CancellationToken cancellationToken) {
            _log.Add($"undo:{arguments.Reservation}");

            return Task.CompletedTask;
        }
    }

    public sealed class FailActivity(SlipLog log) : IActivity<SlipArguments> {
        private readonly SlipLog _log = log ?? throw new ArgumentNullException(nameof(log));

        public Task ExecuteAsync(SlipArguments arguments, CancellationToken cancellationToken) {
            _log.Add("fail");

            return Task.FromException(new InvalidOperationException("step-2-failed"));
        }

        public Task CompensateAsync(SlipArguments arguments, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class NeverActivity(SlipLog log) : IActivity<SlipArguments> {
        private readonly SlipLog _log = log ?? throw new ArgumentNullException(nameof(log));

        public Task ExecuteAsync(SlipArguments arguments, CancellationToken cancellationToken) {
            _log.Add("ship");

            return Task.CompletedTask;
        }

        public Task CompensateAsync(SlipArguments arguments, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class ReserveFailCompensationActivity(SlipLog log) : IActivity<SlipArguments> {
        private readonly SlipLog _log = log ?? throw new ArgumentNullException(nameof(log));

        public Task ExecuteAsync(SlipArguments arguments, CancellationToken cancellationToken) {
            _log.Add("reserve");
            arguments.Reservation = "r-1";

            return Task.CompletedTask;
        }

        public Task CompensateAsync(SlipArguments arguments, CancellationToken cancellationToken) {
            _log.Add("undo:reserve");

            return Task.FromException(new InvalidOperationException("undo-failed"));
        }
    }

    public sealed class FlakyActivity(SlipLog log, AttemptState attempts) : IActivity<SlipArguments> {
        private readonly SlipLog _log = log ?? throw new ArgumentNullException(nameof(log));
        private readonly AttemptState _attempts = attempts ?? throw new ArgumentNullException(nameof(attempts));

        public Task ExecuteAsync(SlipArguments arguments, CancellationToken cancellationToken) {
            var attempt = _attempts.Next();
            _log.Add($"attempt:{attempt}");

            return attempt < 3
                ? Task.FromException(new InvalidOperationException("transient"))
                : Task.CompletedTask;
        }

        public Task CompensateAsync(SlipArguments arguments, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class SlowActivity : IActivity<SlipArguments> {
        public async Task ExecuteAsync(SlipArguments arguments, CancellationToken cancellationToken) =>
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);

        public Task CompensateAsync(SlipArguments arguments, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}