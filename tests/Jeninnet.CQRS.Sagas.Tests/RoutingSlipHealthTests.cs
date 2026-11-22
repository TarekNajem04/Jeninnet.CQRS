//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.Tests;

/// <summary>
/// CQRS-58 health: a running or compensating routing slip older than the threshold is unhealthy;
/// terminal rows are ignored.
/// </summary>
[TestClass]
public sealed class RoutingSlipHealthTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task StuckRunningSlip_IsUnhealthy_AndTerminalSlipIsIgnoredAsync() {
        // Arrange
        var clock = new TestClock();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddCqrsRoutingSlips();
        services.AddCqrsRoutingSlipHealthChecks(options => options.StuckAfter = TimeSpan.FromMinutes(5));
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IRoutingSlipStore>();
        var health = provider.GetRequiredService<HealthCheckService>();
        var now = clock.GetUtcNow();

        await store.CreateAsync(NewSlip("slip-1", RoutingSlipStatus.Running, now), TestContext!.CancellationToken).ConfigureAwait(false);
        clock.Advance(TimeSpan.FromMinutes(10));

        // Act
        var stuck = await health.CheckHealthAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => stuck.Entries["routing-slips"].Status.Should().Be(HealthStatus.Unhealthy));
        MSTestAssertions.That(() => stuck.Entries["routing-slips"].Data["active"].Should().Be(1));

        // Arrange: replace the active row with an older terminal row.
        await store.DeleteAsync("slip-1", TestContext!.CancellationToken).ConfigureAwait(false);
        await store.CreateAsync(NewSlip("slip-2", RoutingSlipStatus.Completed, now.AddHours(-1)), TestContext!.CancellationToken).ConfigureAwait(false);

        // Act
        var healthy = await health.CheckHealthAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => healthy.Entries["routing-slips"].Status.Should().Be(HealthStatus.Healthy));
        MSTestAssertions.That(() => healthy.Entries["routing-slips"].Data["active"].Should().Be(0));
    }

    private static RoutingSlipRecord NewSlip(string slipId, RoutingSlipStatus status, DateTimeOffset updatedAtUtc) =>
        new() {
            SlipId = slipId,
            DefinitionName = "health",
            ArgumentsType = typeof(object).AssemblyQualifiedName ?? nameof(Object),
            ArgumentsPayload = [],
            Status = status,
            CreatedAtUtc = updatedAtUtc,
            UpdatedAtUtc = updatedAtUtc,
        };
}