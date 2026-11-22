//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

[TestClass]
public class CqrsHealthCheckTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task Healthy_WhenBrokerRegistered_AndBreakersClosedAsync() {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddCqrsHealthChecks();
        var provider = services.BuildServiceProvider();

        // Act: run through HealthCheckService, like the /healthz endpoint does.
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => report.Entries.Should().ContainKey("cqrs"));
        MSTestAssertions.That(() => report.Entries["cqrs"].Status.Should().Be(HealthStatus.Healthy));
    }

    [TestMethod]
    public async Task Unhealthy_WhenNoBrokerRegisteredAsync() {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCqrsHealthChecks();
        var provider = services.BuildServiceProvider();

        // Act
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => report.Entries.Should().ContainKey("cqrs"));
        MSTestAssertions.That(() => report.Entries["cqrs"].Status.Should().Be(HealthStatus.Unhealthy));
    }

    [TestMethod]
    public async Task Degraded_WhenBreakerOpen_WithBreakerNameInDataAsync() {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddCqrsResilienceBehavior(new RetryBehaviorOptions {
            RetryCount = 1,
            EnableCircuitBreaker = true,
            CircuitBreakerFailureThreshold = 1,
            CircuitBreakerOpenSeconds = 60
        });
        services.AddCqrsHealthChecks();
        var provider = services.BuildServiceProvider();

        var behavior = provider.GetRequiredService<IPipelineBehavior<TestCommand, string>>();
        try {
            await behavior.HandleAsync(new TestCommand("Run"), (_, __) => Task.FromException<string>(new InvalidOperationException("boom")), default);
        }
        catch(InvalidOperationException) {
            // Expected: the failure that trips the breaker.
        }

        // Act: run through HealthCheckService, like the /healthz endpoint does.
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(TestContext!.CancellationToken);
        var result = report.Entries["cqrs"];

        // Assert: degraded, never unhealthy — other requests still flow.
        MSTestAssertions.That(() => result.Status.Should().Be(HealthStatus.Degraded));
        MSTestAssertions.That(() => ((List<string>)result.Data["openBreakers"]).Should().Contain(typeof(TestCommand).FullName!));
        MSTestAssertions.That(() => result.Data["openBreakerCount"].Should().Be(1));
    }

    private sealed record TestCommand(string Message) : ICommand<string>;
}
