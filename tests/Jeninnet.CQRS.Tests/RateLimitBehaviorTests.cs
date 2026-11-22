//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

[TestClass]
public class RateLimitBehaviorTests {
    private static RateLimitOptions Options(int permits, int queue = 0, int windowSeconds = 60) =>
        new() {
            PermitLimit = permits,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueLimit = queue
        };

    private static IPipelineBehavior<TestCommand, string> Behavior(RateLimitOptions options) {
        var services = new ServiceCollection();
        services.AddCqrsRateLimiting(options);

        return services.BuildServiceProvider().GetRequiredService<IPipelineBehavior<TestCommand, string>>();
    }

    private static Task<string> Succeeding(TestCommand _, CancellationToken __) => Task.FromResult("ok");

    [TestMethod]
    public async Task AdmitsUnderLimitAsync() {
        // Arrange
        var behavior = Behavior(Options(permits: 2));
        var invocations = 0;
        Task<string> counting(TestCommand cmd, CancellationToken ct) {
            invocations++;
            return Succeeding(cmd, ct);
        }

        // Act
        await behavior.HandleAsync(new TestCommand("A"), counting, default);
        var result = await behavior.HandleAsync(new TestCommand("B"), counting, default);

        // Assert
        MSTestAssertions.That(() => result.Should().Be("ok"));
        MSTestAssertions.That(() => invocations.Should().Be(2));
    }

    [TestMethod]
    public async Task RejectsOverLimit_WithoutInvokingHandlerAsync() {
        // Arrange
        var behavior = Behavior(Options(permits: 1));
        var invocations = 0;
        Task<string> counting(TestCommand cmd, CancellationToken ct) {
            invocations++;
            return Succeeding(cmd, ct);
        }

        await behavior.HandleAsync(new TestCommand("A"), counting, default);

        // Act
        RateLimitRejectedException? thrown = null;
        try {
            await behavior.HandleAsync(new TestCommand("B"), counting, default);
        }
        catch(RateLimitRejectedException ex) {
            thrown = ex;
        }

        // Assert: fail fast with the partition key; the handler never ran again.
        MSTestAssertions.That(() => thrown.Should().NotBeNull());
        MSTestAssertions.That(() => thrown!.Key.Should().Be(typeof(TestCommand).FullName));
        MSTestAssertions.That(() => invocations.Should().Be(1));
    }

    [TestMethod]
    public async Task KeySelector_IsolatesPartitionsAsync() {
        // Arrange
        var options = Options(permits: 1);
        options.KeySelector = static request => ((TenantCommand)request).Tenant;
        var services = new ServiceCollection();
        services.AddCqrsRateLimiting(options);
        var behavior = services.BuildServiceProvider().GetRequiredService<IPipelineBehavior<TenantCommand, string>>();
        static Task<string> succeeding(TenantCommand cmd, CancellationToken _) => Task.FromResult($"ok:{cmd.Tenant}");

        // Act: same key twice trips; a different key flows.
        await behavior.HandleAsync(new TenantCommand("A"), succeeding, default);
        await behavior.HandleAsync(new TenantCommand("B"), succeeding, default);
        var rejected = await CaptureAsync(() => behavior.HandleAsync(new TenantCommand("A"), succeeding, default));

        // Assert
        MSTestAssertions.That(() => rejected.Should().BeOfType<RateLimitRejectedException>());
        MSTestAssertions.That(() => ((RateLimitRejectedException)rejected!).Key.Should().Be("A"));
    }

    [TestMethod]
    public async Task CancelledAcquire_Throws_WithoutInvokingHandlerAsync() {
        // Arrange: the single permit is held by a blocked handler; the queued acquire is pre-cancelled.
        var behavior = Behavior(Options(permits: 1, queue: 1));
        var gate = new TaskCompletionSource<string>();
        var invocations = 0;
        Task<string> blocking(TestCommand _, CancellationToken __) {
            Interlocked.Increment(ref invocations);
            return gate.Task;
        }

        var holding = behavior.HandleAsync(new TestCommand("Hold"), blocking, default);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        OperationCanceledException? thrown = null;
        try {
            await behavior.HandleAsync(new TestCommand("Queued"), blocking, cts.Token);
        }
        catch(OperationCanceledException ex) {
            thrown = ex;
        }
        finally {
            gate.TrySetResult("released");
            await holding;
        }

        // Assert
        MSTestAssertions.That(() => thrown.Should().NotBeNull());
        MSTestAssertions.That(() => invocations.Should().Be(1));
    }

    [TestMethod]
    public void Registration_SharesOneLimiter() {
        // Arrange
        var services = new ServiceCollection();
        services.AddCqrsRateLimiting(Options(permits: 2));
        var provider = services.BuildServiceProvider();

        // Act
        var first = provider.GetRequiredService<PartitionedRateLimiter<string>>();
        var second = provider.GetRequiredService<PartitionedRateLimiter<string>>();
        var behaviors = provider.GetServices<IPipelineBehavior<TestCommand, string>>().ToList();

        // Assert: one shared limiter (cross-dispatch state), transient behaviors.
        MSTestAssertions.That(() => ReferenceEquals(first, second).Should().BeTrue());
        MSTestAssertions.That(() => behaviors.Should().NotBeEmpty());
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> act) {
        try {
            await act();
            return null;
        }
        catch(Exception ex) {
            return ex;
        }
    }

    private sealed record TestCommand(string Message) : ICommand<string>;

    private sealed record TenantCommand(string Tenant) : ICommand<string>;
}
