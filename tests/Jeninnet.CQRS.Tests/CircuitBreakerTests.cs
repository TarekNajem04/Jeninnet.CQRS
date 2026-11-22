//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// Circuit-breaker tests for <see cref="RetryBehavior{TRequest, TResponse}"/> (CQRS-15).
/// The first four tests characterize the pre-hardening behavior; the rest prove the new
/// half-open state machine (single trial, waiting group, events, hammer, shared state).
/// </summary>
[TestClass]
public class CircuitBreakerTests {
    public TestContext? TestContext { get; set; }
    private static RetryBehaviorOptions BreakerOptions(int threshold = 2, int openSeconds = 60) =>
        new() {
            RetryCount = 1,
            BaseDelayMilliseconds = 0,
            EnableCircuitBreaker = true,
            TimeoutSeconds = 0,
            CircuitBreakerFailureThreshold = threshold,
            CircuitBreakerOpenSeconds = openSeconds
        };

    private static Task<string> Failing(TestCommand _, CancellationToken __) =>
        Task.FromException<string>(new InvalidOperationException("boom"));

    private static Task<string> Succeeding(TestCommand _, CancellationToken __) =>
        Task.FromResult("ok");

    private static async Task<Exception?> CaptureAsync(Func<Task> act) {
        try {
            await act();
            return null;
        }
        catch(Exception ex) {
            return ex;
        }
    }

    [TestMethod]
    public async Task OpensAfterThreshold_WithoutInvokingHandlerAsync() {
        // Arrange
        var behavior = new RetryBehavior<TestCommand, string>(Options.Create(BreakerOptions()));
        var invocations = 0;
        Task<string> counting(TestCommand cmd, CancellationToken ct) {
            invocations++;
            return Failing(cmd, ct);
        }

        // Act: two failures trip the breaker (threshold = 2)
        await CaptureAsync(() => behavior.HandleAsync(new TestCommand("Run"), counting, default));
        await CaptureAsync(() => behavior.HandleAsync(new TestCommand("Run"), counting, default));
        var third = await CaptureAsync(() => behavior.HandleAsync(new TestCommand("Run"), counting, default));

        // Assert: the third call fails fast with the documented breaker message.
        MSTestAssertions.That(() => third.Should().NotBeNull());
        MSTestAssertions.That(() => third.Should().BeOfType<InvalidOperationException>());
        MSTestAssertions.That(() => third!.Message.Should().Be("Circuit breaker is open"));
        MSTestAssertions.That(() => invocations.Should().Be(2));
    }

    [TestMethod]
    public async Task DisabledBreaker_NeverOpensAsync() {
        // Arrange
        var options = BreakerOptions();
        options.EnableCircuitBreaker = false;
        var behavior = new RetryBehavior<TestCommand, string>(Options.Create(options));
        var invocations = 0;
        Task<string> counting(TestCommand cmd, CancellationToken ct) {
            invocations++;
            return Failing(cmd, ct);
        }

        // Act: five consecutive failures with the breaker disabled.
        for(var i = 0; i < 5; i++) {
            await CaptureAsync(() => behavior.HandleAsync(new TestCommand("Run"), counting, default));
        }

        // Assert: every call reached the handler; the breaker never engaged.
        MSTestAssertions.That(() => invocations.Should().Be(5));
        MSTestAssertions.That(() => behavior.BreakerState.State.Should().Be(CircuitBreakerState.Closed));
    }

    [TestMethod]
    public async Task SuccessResetsConsecutiveCountAsync() {
        // Arrange: threshold = 2 -> fail, success, fail, fail opens only on the 4th call.
        var behavior = new RetryBehavior<TestCommand, string>(Options.Create(BreakerOptions()));

        // Act
        await CaptureAsync(() => behavior.HandleAsync(new TestCommand("Run"), Failing, default));
        await behavior.HandleAsync(new TestCommand("Run"), Succeeding, default);
        await CaptureAsync(() => behavior.HandleAsync(new TestCommand("Run"), Failing, default));
        var fourth = await CaptureAsync(() => behavior.HandleAsync(new TestCommand("Run"), Failing, default));
        var fifth = await CaptureAsync(() => behavior.HandleAsync(new TestCommand("Run"), Failing, default));

        // Assert: without the reset the 3rd call would already have opened the breaker,
        // so the fail-fast breaker error only appears on the 5th call.
        MSTestAssertions.That(() => fourth.Should().BeOfType<InvalidOperationException>());
        MSTestAssertions.That(() => fourth!.Message.Should().Be("boom"));
        MSTestAssertions.That(() => fifth.Should().BeOfType<InvalidOperationException>());
        MSTestAssertions.That(() => fifth!.Message.Should().Be("Circuit breaker is open"));
    }

    [TestMethod]
    public async Task CooldownAdmitsSingleTrial_ThatClosesOnSuccessAsync() {
        // Arrange: threshold = 1 opens immediately; short cooldown for the test.
        var behavior = new RetryBehavior<TestCommand, string>(Options.Create(BreakerOptions(threshold: 1, openSeconds: 1)));
        await CaptureAsync(() => behavior.HandleAsync(new TestCommand("Run"), Failing, default));
        MSTestAssertions.That(() => behavior.BreakerState.State.Should().Be(CircuitBreakerState.Open));

        // Act: after the cooldown the next call is admitted as a trial and succeeds.
        await Task.Delay(1100, TestContext!.CancellationToken);
        var result = await behavior.HandleAsync(new TestCommand("Run"), Succeeding, default);

        // Assert: the trial closed the breaker again.
        MSTestAssertions.That(() => result.Should().Be("ok"));
        MSTestAssertions.That(() => behavior.BreakerState.State.Should().Be(CircuitBreakerState.Closed));
        MSTestAssertions.That(() => behavior.BreakerState.ConsecutiveFailures.Should().Be(0));
    }

    [TestMethod]
    public async Task TrialFailure_ReopensWithoutExtraInvocationsAsync() {
        // Arrange
        var behavior = new RetryBehavior<TestCommand, string>(Options.Create(BreakerOptions(threshold: 1, openSeconds: 1)));
        var invocations = 0;
        Task<string> counting(TestCommand cmd, CancellationToken ct) {
            invocations++;
            return Failing(cmd, ct);
        }

        await CaptureAsync(() => behavior.HandleAsync(new TestCommand("Run"), counting, default));
        await Task.Delay(1100, TestContext!.CancellationToken);

        // Act: 10 concurrent calls after the cooldown — exactly one may trial.
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => CaptureAsync(() => behavior.HandleAsync(new TestCommand("Run"), counting, default))));

        // Assert: one trial invocation; one handler error; nine fail-fast breaker errors.
        MSTestAssertions.That(() => invocations.Should().Be(2));
        MSTestAssertions.That(() => results.Count(e => e is InvalidOperationException ex && ex.Message == "boom").Should().Be(1));
        MSTestAssertions.That(() => results.Count(e => e is InvalidOperationException ex && ex.Message == "Circuit breaker is open").Should().Be(9));
        MSTestAssertions.That(() => behavior.BreakerState.State.Should().Be(CircuitBreakerState.Open));
    }

    [TestMethod]
    public async Task ConcurrentTrialSuccess_AdmitsWholeGroupAsync() {
        // Arrange
        var behavior = new RetryBehavior<TestCommand, string>(Options.Create(BreakerOptions(threshold: 1, openSeconds: 1)));
        var transitions = new System.Collections.Concurrent.ConcurrentQueue<(CircuitBreakerState Old, CircuitBreakerState New)>();
        behavior.BreakerStateChanged += (_, e) => transitions.Enqueue((e.OldState, e.NewState));

        await CaptureAsync(() => behavior.HandleAsync(new TestCommand("Run"), Failing, default));
        await Task.Delay(1100, TestContext!.CancellationToken);

        static Task<string> slowSuccess(TestCommand _, CancellationToken ct) => Task.Delay(300, ct).ContinueWith(_ => "ok", ct);

        // Act: 10 concurrent calls; the trial succeeds while the group waits.
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => behavior.HandleAsync(new TestCommand("Run"), slowSuccess, default)));

        // Assert: everyone succeeded; the breaker cycled Open -> HalfOpen -> Closed.
        MSTestAssertions.That(() => results.Should().Equal(Enumerable.Repeat("ok", 10)));
        var ordered = transitions.ToArray();
        MSTestAssertions.That(() => ordered.Should().ContainInOrder(
            (CircuitBreakerState.Closed, CircuitBreakerState.Open),
            (CircuitBreakerState.Open, CircuitBreakerState.HalfOpen),
            (CircuitBreakerState.HalfOpen, CircuitBreakerState.Closed)));
    }

    [TestMethod]
    public async Task OpenBreaker_RejectsHammer_WithoutTornStateAsync() {
        // Arrange: open the breaker first, with a cooldown that never elapses mid-test.
        var behavior = new RetryBehavior<TestCommand, string>(Options.Create(BreakerOptions(threshold: 2, openSeconds: 60)));
        var invocations = 0;
        Task<string> counting(TestCommand cmd, CancellationToken ct) {
            Interlocked.Increment(ref invocations);
            return Failing(cmd, ct);
        }

        await CaptureAsync(() => behavior.HandleAsync(new TestCommand("Run"), counting, default));
        await CaptureAsync(() => behavior.HandleAsync(new TestCommand("Run"), counting, default));
        MSTestAssertions.That(() => behavior.BreakerState.State.Should().Be(CircuitBreakerState.Open));

        // Act: 100 parallel calls against the open breaker.
        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => CaptureAsync(() => behavior.HandleAsync(new TestCommand("Run"), counting, default))));

        // Assert: no hangs, no extra invocations, every call failed fast, state consistent.
        MSTestAssertions.That(() => results.Should().HaveCount(100));
        MSTestAssertions.That(() => invocations.Should().Be(2));
        MSTestAssertions.That(() => results.All(e => e is InvalidOperationException ex && ex.Message == "Circuit breaker is open").Should().BeTrue());
        MSTestAssertions.That(() => behavior.BreakerState.State.Should().Be(CircuitBreakerState.Open));
        MSTestAssertions.That(() => behavior.BreakerState.ConsecutiveFailures.Should().BeGreaterThanOrEqualTo(2));
    }

    [TestMethod]
    public async Task AllSuccess_Hammer_KeepsBreakerClosedAsync() {
        // Arrange
        var behavior = new RetryBehavior<TestCommand, string>(Options.Create(BreakerOptions(threshold: 2, openSeconds: 60)));

        // Act: 50 parallel successful calls.
        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => behavior.HandleAsync(new TestCommand("Run"), Succeeding, default)));

        // Assert
        MSTestAssertions.That(() => results.Should().HaveCount(50));
        MSTestAssertions.That(() => results.All(r => r == "ok").Should().BeTrue());
        MSTestAssertions.That(() => behavior.BreakerState.State.Should().Be(CircuitBreakerState.Closed));
        MSTestAssertions.That(() => behavior.BreakerState.ConsecutiveFailures.Should().Be(0));
    }

    [TestMethod]
    public void SingletonRegistration_SharesBreakerAcrossDispatches() {
        // Arrange
        var services = new ServiceCollection();
        services.AddCqrsResilienceBehavior(BreakerOptions(threshold: 1, openSeconds: 60));

        // Act
        var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IPipelineBehavior<TestCommand, string>>();
        var second = provider.GetRequiredService<IPipelineBehavior<TestCommand, string>>();

        // Assert: one shared instance per closed generic — the breaker actually trips.
        MSTestAssertions.That(() => ReferenceEquals(first, second).Should().BeTrue());
    }

    [TestMethod]
    public async Task SharedBreaker_TripsAcrossResolutionsAsync() {
        // Arrange
        var services = new ServiceCollection();
        services.AddCqrsResilienceBehavior(BreakerOptions(threshold: 1, openSeconds: 60));
        var provider = services.BuildServiceProvider();

        // Act: fail through one resolution, call through another.
        var first = provider.GetRequiredService<IPipelineBehavior<TestCommand, string>>();
        await CaptureAsync(() => first.HandleAsync(new TestCommand("Run"), Failing, default));
        var second = provider.GetRequiredService<IPipelineBehavior<TestCommand, string>>();
        var result = await CaptureAsync(() => second.HandleAsync(new TestCommand("Run"), Succeeding, default));

        // Assert: the second resolution sees the open breaker (a transient would be closed).
        MSTestAssertions.That(() => result.Should().BeOfType<InvalidOperationException>());
        MSTestAssertions.That(() => ((InvalidOperationException)result!).Message.Should().Be("Circuit breaker is open"));
    }

    private sealed record TestCommand(string Message) : ICommand<string>;
}