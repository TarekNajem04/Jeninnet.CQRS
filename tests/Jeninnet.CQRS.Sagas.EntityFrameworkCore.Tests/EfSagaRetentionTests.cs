//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.EntityFrameworkCore.Tests;

/// <summary>CQRS-64 saga retention: terminal sagas archive after the cutoff; active sagas stay.</summary>
[TestClass]
public sealed class EfSagaRetentionTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task SagaRetention_ArchivesOnlyTerminalSagasPastTheCutoffAsync() {
        // Arrange
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var file = Path.Combine(Path.GetTempPath(), $"cqrs64-saga-retention-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddCqrsEfSagas<OrderSaga>(db => db.UseSqlite($"Data Source={file}"));
        services.AddCqrsEfSagaRetention(configure: options => {
            options.DryRun = false;
            options.AddPolicy(RetentionStoreNames.Sagas, new RetentionPolicy {
                Retention = TimeSpan.FromDays(1),
            });
        });

        await using var provider = services.BuildServiceProvider();
        using(var scope = provider.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SagaDbContext>>().CreateDbContext();
            db.Database.EnsureCreated();
        }

        var store = provider.GetRequiredService<ISagaStore>();
        clock.SetUtcNow(clock.GetUtcNow().AddDays(-10));
        await store.SaveAsync(
            "saga-old",
            typeof(OrderSagaState),
            new OrderSagaState { CurrentState = "Completed", OrderId = "old" },
            0,
            SagaProgress.Empty,
            TestContext!.CancellationToken).ConfigureAwait(false);

        clock.SetUtcNow(clock.GetUtcNow().AddDays(10));
        await store.SaveAsync(
            "saga-fresh",
            typeof(OrderSagaState),
            new OrderSagaState { CurrentState = "Completed", OrderId = "fresh" },
            0,
            SagaProgress.Empty,
            TestContext!.CancellationToken).ConfigureAwait(false);

        // Act
        var results = await provider.GetRequiredService<RetentionSweeper>()
            .SweepOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        var result = results.Single(candidate => candidate.Store == RetentionStoreNames.Sagas);
        var oldSaga = await store.LoadAsync("saga-old", typeof(OrderSagaState), TestContext!.CancellationToken).ConfigureAwait(false);
        var freshSaga = await store.LoadAsync("saga-fresh", typeof(OrderSagaState), TestContext!.CancellationToken).ConfigureAwait(false);
        using(var scope = provider.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SagaDbContext>>().CreateDbContext();

            MSTestAssertions.That(() => db.SagaArchive.CountAsync(TestContext!.CancellationToken).GetAwaiter().GetResult().Should().Be(1));
        }

        // Assert
        MSTestAssertions.That(() => result.Archived.Should().Be(1));
        MSTestAssertions.That(() => oldSaga.Should().BeNull());
        MSTestAssertions.That(() => freshSaga.Should().NotBeNull());
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        internal void SetUtcNow(DateTimeOffset now) => _now = now;
    }
}
