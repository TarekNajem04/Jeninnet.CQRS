//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.EntityFrameworkCore.Tests;

/// <summary>
/// Characterization tests for CQRS-48 on EF Core: a saga survives a host restart, an unknown
/// correlation id creates the initial state, and two writers using the same version produce one
/// winner plus <see cref="SagaConcurrencyException"/>.
/// </summary>
[TestClass]
public sealed class EfSagaStoreTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task TwoEventSaga_SurvivesRestart_AndPublishesOnceAsync() {
        // Arrange: the first host stages the initial state and exits.
        var file = NewDbFile();
        var log = new List<string>();

        await using(var first = BuildHost(file, log)) {
            await first.GetRequiredService<ICqrsBroker>()
                .PublishAsync(new OrderPlaced("order-1"), TestContext!.CancellationToken).ConfigureAwait(false);
        }

        // Act: a fresh host resumes the row and completes the saga.
        await using var second = BuildHost(file, log);
        await second.GetRequiredService<ICqrsBroker>()
            .PublishAsync(new PaymentTaken("order-1", "payment-1"), TestContext!.CancellationToken).ConfigureAwait(false);
        var record = await second.GetRequiredService<ISagaStore>()
            .LoadAsync("order-1", typeof(OrderSagaState), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => record.Should().NotBeNull());
        MSTestAssertions.That(() => record!.State.CurrentState.Should().Be("Completed"));
        MSTestAssertions.That(() => ((OrderSagaState)record!.State).PaymentId.Should().Be("payment-1"));
        MSTestAssertions.That(() => record!.Version.Should().Be(2));
        MSTestAssertions.That(() => log.Should().Equal(["reserve:order-1", "ship:order-1"]));
    }

    [TestMethod]
    public async Task UnknownCorrelationId_CreatesTheInitialStateAsync() {
        // Arrange
        await using var provider = BuildHost(NewDbFile(), []);
        var store = provider.GetRequiredService<ISagaStore>();

        // Act
        var result = await provider.GetRequiredService<SagaDispatcher>()
            .DispatchAsync(new OrderPlaced("new-order"), typeof(OrderPlaced), TestContext!.CancellationToken).ConfigureAwait(false);
        var record = await store.LoadAsync("new-order", typeof(OrderSagaState), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => result!.Status.Should().Be(SagaDispatchStatus.Created));
        MSTestAssertions.That(() => record.Should().NotBeNull());
        MSTestAssertions.That(() => record!.State.CurrentState.Should().Be("AwaitingPayment"));
        MSTestAssertions.That(() => record!.Version.Should().Be(1));
    }

    [TestMethod]
    public async Task Compensation_RunsInReverseOrder_AndResumesWithoutRepeatingCompletedStepsAsync() {
        // Arrange: two compensable steps and a failure event that fails on the oldest undo.
        var file = NewDbFile();
        var log = new List<string>();
        var state = new CompensationSwitch { FailOn = "one" };

        await using(var first = BuildCompensationHost(file, log, state)) {
            var broker = first.GetRequiredService<ICqrsBroker>();
            await broker.PublishAsync(new CompensationStarted("comp-1"), TestContext!.CancellationToken).ConfigureAwait(false);

            var thrown = await CaptureAsync(() => broker.PublishAsync(new CompensationFailed("comp-1"), TestContext!.CancellationToken)).ConfigureAwait(false);
            var partial = await first.GetRequiredService<ISagaStore>()
                .LoadAsync("comp-1", typeof(CompensationSagaState), TestContext!.CancellationToken).ConfigureAwait(false);

            // Assert: "two" ran, "one" threw, and the failure transition was not committed.
            MSTestAssertions.That(() => thrown.Should().NotBeNull());
            MSTestAssertions.That(() => log.Should().Equal(["undo:two"]));
            MSTestAssertions.That(() => partial!.State.CurrentState.Should().Be("Awaiting"));
            MSTestAssertions.That(() => partial!.ProgressState.CompensatedSteps.Should().Contain("step-2"));
        }

        // Act: a fresh host retries the failure event; the completed "two" is skipped.
        state.FailOn = null;
        await using var second = BuildCompensationHost(file, log, state);
        await second.GetRequiredService<ICqrsBroker>()
            .PublishAsync(new CompensationFailed("comp-1"), TestContext!.CancellationToken).ConfigureAwait(false);
        var completed = await second.GetRequiredService<ISagaStore>()
            .LoadAsync("comp-1", typeof(CompensationSagaState), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => log.Should().Equal(["undo:two", "undo:one"]));
        MSTestAssertions.That(() => completed!.State.CurrentState.Should().Be("Faulted"));
        MSTestAssertions.That(() => completed!.ProgressState.CompensatedSteps.Should().HaveCount(2));
    }

    [TestMethod]
    public async Task StaleVersion_LosesTheWrite_AndSurfacesSagaConcurrencyExceptionAsync() {
        // Arrange: two readers load the same version.
        await using var provider = BuildHost(NewDbFile(), []);
        var store = provider.GetRequiredService<ISagaStore>();
        await store.SaveAsync(
            "stale",
            typeof(OrderSagaState),
            new OrderSagaState { CurrentState = "AwaitingPayment", OrderId = "stale" },
            0,
            TestContext!.CancellationToken).ConfigureAwait(false);

        var first = await store.LoadAsync("stale", typeof(OrderSagaState), TestContext!.CancellationToken).ConfigureAwait(false);
        var second = await store.LoadAsync("stale", typeof(OrderSagaState), TestContext!.CancellationToken).ConfigureAwait(false);

        // Act: the first writer commits at version 1; the second still expects 1.
        first!.State.CurrentState = "Completed";
        await store.SaveAsync("stale", typeof(OrderSagaState), first.State, first.Version, TestContext!.CancellationToken).ConfigureAwait(false);
        var thrown = await CaptureAsync(() => store.SaveAsync(
            "stale",
            typeof(OrderSagaState),
            second!.State,
            second.Version,
            TestContext!.CancellationToken)).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<SagaConcurrencyException>());
        MSTestAssertions.That(() => ((SagaConcurrencyException)thrown!).ActualVersion.Should().Be(2));
    }

    private static string NewDbFile() =>
        Path.Combine(Path.GetTempPath(), $"cqrs48-saga-{Guid.NewGuid():N}.db");

    private static ServiceProvider BuildHost(string file, List<string> log) {
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddTransient<ICommandHandler<ReserveStock>, ReserveStockHandler>();
        services.AddTransient<ICommandHandler<ShipOrder>, ShipOrderHandler>();
        services.AddCqrsEfSagas<OrderSaga>(db => db.UseSqlite($"Data Source={file}"));
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SagaDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();

        return provider;
    }

    private static ServiceProvider BuildCompensationHost(string file, List<string> log, CompensationSwitch state) {
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddSingleton(state);
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddTransient<ICommandHandler<CompensationCommand>, CompensationCommandHandler>();
        services.AddCqrsEfSagas<CompensatingSaga>(db => db.UseSqlite($"Data Source={file}"));
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SagaDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();

        return provider;
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

    private sealed class CompensationSwitch {
        internal string? FailOn { get; set; }
    }

    private sealed class CompensationCommandHandler(List<string> log, CompensationSwitch state) : ICommandHandler<CompensationCommand> {
        public Task<Unit> HandleAsync(CompensationCommand command, CancellationToken cancellationToken) {
            if(string.Equals(state.FailOn, command.Name, StringComparison.Ordinal)) {
                throw new InvalidOperationException($"compensation {command.Name} failed");
            }

            log.Add($"undo:{command.Name}");

            return Task.FromResult(Unit.Value);
        }
    }

    private sealed class ReserveStockHandler(List<string> log) : ICommandHandler<ReserveStock> {
        public Task<Unit> HandleAsync(ReserveStock command, CancellationToken cancellationToken) {
            log.Add($"reserve:{command.OrderId}");

            return Task.FromResult(Unit.Value);
        }
    }

    private sealed class ShipOrderHandler(List<string> log) : ICommandHandler<ShipOrder> {
        public Task<Unit> HandleAsync(ShipOrder command, CancellationToken cancellationToken) {
            log.Add($"ship:{command.OrderId}");

            return Task.FromResult(Unit.Value);
        }
    }
}
