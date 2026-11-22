//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// Characterization tests for CQRS-31 health contributions: deferred backlog and outbox lag
/// flip at the documented boundaries, and absent stores contribute no checks.
/// </summary>
[TestClass]
public class DurabilityHealthCheckTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task DeferredBacklog_FlipsAtDocumentedBoundariesAsync() {
        // Arrange: degraded at 2, unhealthy at 4.
        var clock = new ManualClock();
        await using var provider = BuildDeferredHost(clock, degraded: 2, unhealthy: 4);
        var store = provider.GetRequiredService<IDeferredStore>();
        var health = provider.GetRequiredService<HealthCheckService>();

        // Act / Assert: 1 is healthy, 2 is degraded, 4 is unhealthy.
        await StageDeferredAsync(store, clock, 1);
        MSTestAssertions.That(() => (health.CheckHealthAsync(TestContext!.CancellationToken).GetAwaiter().GetResult()).Entries["deferred"].Status.Should().Be(HealthStatus.Healthy));

        await StageDeferredAsync(store, clock, 1);
        MSTestAssertions.That(() => (health.CheckHealthAsync(TestContext!.CancellationToken).GetAwaiter().GetResult()).Entries["deferred"].Status.Should().Be(HealthStatus.Degraded));

        await StageDeferredAsync(store, clock, 2);
        MSTestAssertions.That(() => (health.CheckHealthAsync(TestContext!.CancellationToken).GetAwaiter().GetResult()).Entries["deferred"].Status.Should().Be(HealthStatus.Unhealthy));
    }

    [TestMethod]
    public async Task OutboxLag_FlipsAtDocumentedBoundariesAsync() {
        // Arrange: degraded after 10s, unhealthy after 60s.
        var clock = new ManualClock();
        await using var provider = BuildOutboxHost(clock, degradedSeconds: 10, unhealthySeconds: 60);
        var store = provider.GetRequiredService<IOutboxStore>();
        var health = provider.GetRequiredService<HealthCheckService>();

        // Empty outbox is healthy.
        MSTestAssertions.That(() => (health.CheckHealthAsync(TestContext!.CancellationToken).GetAwaiter().GetResult()).Entries["outbox"].Status.Should().Be(HealthStatus.Healthy));

        // 5s old: healthy; adding a 20s-old entry makes the oldest 20s: degraded.
        await StageOutboxAsync(store, clock, 5);
        MSTestAssertions.That(() => (health.CheckHealthAsync(TestContext!.CancellationToken).GetAwaiter().GetResult()).Entries["outbox"].Status.Should().Be(HealthStatus.Healthy));

        await StageOutboxAsync(store, clock, 20);
        MSTestAssertions.That(() => (health.CheckHealthAsync(TestContext!.CancellationToken).GetAwaiter().GetResult()).Entries["outbox"].Status.Should().Be(HealthStatus.Degraded));

        // A 70s-old entry crosses the hard cap.
        await StageOutboxAsync(store, clock, 70);
        MSTestAssertions.That(() => (health.CheckHealthAsync(TestContext!.CancellationToken).GetAwaiter().GetResult()).Entries["outbox"].Status.Should().Be(HealthStatus.Unhealthy));
    }

    [TestMethod]
    public async Task AbsentStores_ContributeNoChecksAsync() {
        // Arrange: durability health without any store.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCqrsDurabilityHealthChecks();
        await using var provider = services.BuildServiceProvider();
        var health = provider.GetRequiredService<HealthCheckService>();

        // Act
        var report = await health.CheckHealthAsync(TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => report.Entries.Should().BeEmpty());
    }

    private static ServiceProvider BuildDeferredHost(ManualClock clock, int degraded, int unhealthy) {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDeferredStore>(new InMemoryDeferredStore());
        services.AddSingleton<TimeProvider>(clock);
        services.AddCqrsDurabilityHealthChecks(options => {
            options.DeferredDegradedBacklog = degraded;
            options.DeferredUnhealthyBacklog = unhealthy;
        });

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildOutboxHost(ManualClock clock, int degradedSeconds, int unhealthySeconds) {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOutboxStore>(new InMemoryOutboxStore());
        services.AddSingleton<TimeProvider>(clock);
        services.AddCqrsDurabilityHealthChecks(options => {
            options.OutboxDegradedLag = TimeSpan.FromSeconds(degradedSeconds);
            options.OutboxUnhealthyLag = TimeSpan.FromSeconds(unhealthySeconds);
        });

        return services.BuildServiceProvider();
    }

    private static async Task StageDeferredAsync(IDeferredStore store, TimeProvider clock, int count) {
        for(var index = 0; index < count; index++) {
            await store.StageAsync(new DeferredEnvelope<HealthCommand>(new HealthCommand($"c{index}"), clock.GetUtcNow(), null), default).ConfigureAwait(false);
        }
    }

    private static Task<string> StageOutboxAsync(IOutboxStore store, TimeProvider clock, int ageSeconds) =>
        store.StageAsync(new OutboxNotification<HealthNotification>(new HealthNotification("n"), clock.GetUtcNow().AddSeconds(-ageSeconds)), default);

    private sealed record HealthCommand(string Name) : ICommand;

    private sealed record HealthNotification(string Name) : INotification;

    private sealed class ManualClock : TimeProvider {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
