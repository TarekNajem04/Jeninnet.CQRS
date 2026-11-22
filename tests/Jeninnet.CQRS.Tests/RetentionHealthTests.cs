//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>CQRS-64 retention health: degraded before the first sweep, healthy after one succeeds.</summary>
[TestClass]
public sealed class RetentionHealthTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task RetentionHealth_TracksSweeperFreshnessAsync() {
        // Arrange
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IRetentionStore, EmptyRetentionStore>();
        services.AddCqrsRetention(options => {
            options.DryRun = true;
            options.AddPolicy("test-store", new RetentionPolicy { Retention = TimeSpan.FromDays(1) });
        });
        services.AddCqrsRetentionHealthChecks(options => options.MaxLag = TimeSpan.FromHours(1));

        await using var provider = services.BuildServiceProvider();
        var health = provider.GetRequiredService<HealthCheckService>();
        var sweeper = provider.GetRequiredService<RetentionSweeper>();

        // Act: no successful sweep yet.
        var before = await health.CheckHealthAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        await sweeper.SweepOnceAsync(TestContext.CancellationToken).ConfigureAwait(false);
        var after = await health.CheckHealthAsync(TestContext.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => before.Entries["retention"].Status.Should().Be(HealthStatus.Degraded));
        MSTestAssertions.That(() => after.Entries["retention"].Status.Should().Be(HealthStatus.Healthy));
        MSTestAssertions.That(() => after.Entries["retention"].Data["storeCount"].Should().Be(1));
    }

    private sealed class EmptyRetentionStore : IRetentionStore {
        public string Name => "test-store";

        public Task<RetentionBatchResult> SweepAsync(
            RetentionPolicy policy,
            DateTimeOffset now,
            int batchSize,
            bool dryRun,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new RetentionBatchResult(Name, 0, 0, 0, dryRun));
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
