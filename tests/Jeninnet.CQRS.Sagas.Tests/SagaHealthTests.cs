//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.Tests;

/// <summary>
/// Characterization test for CQRS-49 health: an active saga older than the threshold is unhealthy,
/// while a terminal row is ignored even when it is older.
/// </summary>
[TestClass]
public sealed class SagaHealthTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task StuckActiveSaga_IsUnhealthy_AndCompletedSagaIsIgnoredAsync() {
        // Arrange
        var clock = new TestClock();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddCqrsSagas<HealthSaga>();
        services.AddCqrsSagaHealthChecks(options => options.StuckAfter = TimeSpan.FromMinutes(5));
        await using var provider = services.BuildServiceProvider();
        var health = provider.GetRequiredService<HealthCheckService>();
        var broker = provider.GetRequiredService<ICqrsBroker>();

        await broker.PublishAsync(new HealthStarted("health-1"), TestContext!.CancellationToken).ConfigureAwait(false);
        clock.Advance(TimeSpan.FromMinutes(10));

        // Act
        var stuck = await health.CheckHealthAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => stuck.Entries["sagas"].Status.Should().Be(HealthStatus.Unhealthy));
        MSTestAssertions.That(() => stuck.Entries["sagas"].Data["active"].Should().Be(1));

        // Act: complete the saga; the retained row is terminal and no longer counts.
        await broker.PublishAsync(new HealthFinished("health-1"), TestContext!.CancellationToken).ConfigureAwait(false);
        var healthy = await health.CheckHealthAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => healthy.Entries["sagas"].Status.Should().Be(HealthStatus.Healthy));
        MSTestAssertions.That(() => healthy.Entries["sagas"].Data["active"].Should().Be(0));
    }
}
