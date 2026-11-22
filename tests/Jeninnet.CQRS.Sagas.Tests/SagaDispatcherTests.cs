//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.Tests;

/// <summary>
/// Characterization tests for CQRS-48: a two-event saga transitions and publishes its follow-up
/// commands, out-of-order events are ignored, an unknown correlation id creates the initial
/// state, and a shared store resumes mid-saga after a restart.
/// </summary>
[TestClass]
public sealed class SagaDispatcherTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task TwoEventSaga_TransitionsAndPublishesFollowUpCommandsAsync() {
        // Arrange
        var (provider, store, log) = Build();
        using var _ = provider;
        var broker = provider.GetRequiredService<ICqrsBroker>();

        // Act: the first event creates the saga and reserves stock.
        await broker.PublishAsync(new OrderPlaced("order-1"), TestContext!.CancellationToken).ConfigureAwait(false);
        var pending = await store.LoadAsync("order-1", typeof(OrderSagaState), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => pending.Should().NotBeNull());
        MSTestAssertions.That(() => pending!.State.CurrentState.Should().Be("AwaitingPayment"));
        MSTestAssertions.That(() => ((OrderSagaState)pending!.State).OrderId.Should().Be("order-1"));
        MSTestAssertions.That(() => log.Should().Equal(["reserve:order-1"]));

        // Act: the second event completes the saga and ships the order.
        await broker.PublishAsync(new PaymentTaken("order-1", "payment-1"), TestContext!.CancellationToken).ConfigureAwait(false);
        var completed = await store.LoadAsync("order-1", typeof(OrderSagaState), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => completed!.State.CurrentState.Should().Be("Completed"));
        MSTestAssertions.That(() => ((OrderSagaState)completed!.State).PaymentId.Should().Be("payment-1"));
        MSTestAssertions.That(() => log.Should().Equal(["reserve:order-1", "ship:order-1"]));
    }

    [TestMethod]
    public async Task OutOfOrderEvent_IsIgnoredWithoutChangingStateAsync() {
        // Arrange
        var (provider, store, log) = Build();
        using var _ = provider;
        var dispatcher = provider.GetRequiredService<SagaDispatcher>();

        // Act: payment arrives before the order exists; the transition table has no initial row.
        var unknown = await dispatcher.DispatchAsync(
            new PaymentTaken("missing", "payment-1"),
            typeof(PaymentTaken),
            TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert: no saga was created for a non-initial event.
        MSTestAssertions.That(() => unknown.Should().NotBeNull());
        MSTestAssertions.That(() => unknown!.Status.Should().Be(SagaDispatchStatus.Ignored));
        MSTestAssertions.That(() => store.LoadAsync("missing", typeof(OrderSagaState), TestContext!.CancellationToken).GetAwaiter().GetResult().Should().BeNull());
        MSTestAssertions.That(() => log.Should().BeEmpty());

        // Arrange: complete the saga, then deliver payment again.
        var broker = provider.GetRequiredService<ICqrsBroker>();
        await broker.PublishAsync(new OrderPlaced("order-1"), TestContext!.CancellationToken).ConfigureAwait(false);
        await broker.PublishAsync(new PaymentTaken("order-1", "payment-1"), TestContext!.CancellationToken).ConfigureAwait(false);
        var before = await store.LoadAsync("order-1", typeof(OrderSagaState), TestContext!.CancellationToken).ConfigureAwait(false);

        // Act
        var late = await dispatcher.DispatchAsync(new PaymentTaken("order-1", "payment-2"), typeof(PaymentTaken), TestContext!.CancellationToken).ConfigureAwait(false);
        var after = await store.LoadAsync("order-1", typeof(OrderSagaState), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert: the completed saga ignores the late event and publishes nothing new.
        MSTestAssertions.That(() => late!.Status.Should().Be(SagaDispatchStatus.Ignored));
        MSTestAssertions.That(() => after!.Version.Should().Be(before!.Version));
        MSTestAssertions.That(() => ((OrderSagaState)after!.State).PaymentId.Should().Be("payment-1"));
        MSTestAssertions.That(() => log.Should().Equal(["reserve:order-1", "ship:order-1"]));
    }

    [TestMethod]
    public async Task UnknownCorrelationId_CreatesNewSagaInInitialStateAsync() {
        // Arrange
        var (provider, store, _) = Build();
        using var _ = provider;
        var dispatcher = provider.GetRequiredService<SagaDispatcher>();

        // Act
        var result = await dispatcher.DispatchAsync(
            new OrderPlaced("new-order"),
            typeof(OrderPlaced),
            TestContext!.CancellationToken).ConfigureAwait(false);
        var record = await store.LoadAsync("new-order", typeof(OrderSagaState), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => result!.Status.Should().Be(SagaDispatchStatus.Created));
        MSTestAssertions.That(() => record.Should().NotBeNull());
        MSTestAssertions.That(() => record!.State.CurrentState.Should().Be("AwaitingPayment"));
        MSTestAssertions.That(() => record!.Version.Should().Be(1));
    }

    [TestMethod]
    public async Task Restart_ResumesFromPersistedState_AndPublishesOnceAsync() {
        // Arrange: both hosts share the serialized in-memory store, simulating a process restart.
        var serializer = new JsonMessageSerializer(Options.Create(new JsonMessageSerializerOptions()));
        var sharedStore = new InMemorySagaStore(serializer);
        var log = new List<string>();

        var first = Build(sharedStore, log);
        await using(var firstProvider = first.Provider) {
            await firstProvider.GetRequiredService<ICqrsBroker>()
                .PublishAsync(new OrderPlaced("restart-1"), TestContext!.CancellationToken).ConfigureAwait(false);
        }

        // Act: the second host resumes the persisted row.
        var second = Build(sharedStore, log);
        await using var secondProvider = second.Provider;
        await secondProvider.GetRequiredService<ICqrsBroker>()
            .PublishAsync(new PaymentTaken("restart-1", "payment-1"), TestContext!.CancellationToken).ConfigureAwait(false);
        var record = await sharedStore.LoadAsync("restart-1", typeof(OrderSagaState), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => record!.State.CurrentState.Should().Be("Completed"));
        MSTestAssertions.That(() => log.Should().Equal(["reserve:restart-1", "ship:restart-1"]));
    }

    [TestMethod]
    public void ResolveEventType_MatchesQualifiedFullAndSimpleNames_AndReturnsNullForUnknown() {
        // Arrange
        var (provider, _, _) = Build();
        using var _provider = provider;
        var dispatcher = provider.GetRequiredService<SagaDispatcher>();
        var type = typeof(OrderPlaced);

        // Act
        var assemblyQualified = dispatcher.ResolveEventType(type.AssemblyQualifiedName!);
        var full = dispatcher.ResolveEventType(type.FullName!);
        var simple = dispatcher.ResolveEventType(type.Name);
        var unknown = dispatcher.ResolveEventType("Jeninnet.CQRS.Sagas.Tests.NotAEvent");

        // Assert
        MSTestAssertions.That(() => assemblyQualified.Should().Be(type));
        MSTestAssertions.That(() => full.Should().Be(type));
        MSTestAssertions.That(() => simple.Should().Be(type));
        MSTestAssertions.That(() => unknown.Should().BeNull());
    }

    [TestMethod]
    public async Task DispatchAsync_MissingCorrelation_ThrowsCorrelationExceptionAsync() {
        // Arrange
        var (provider, _, _) = Build();
        using var _provider = provider;
        var dispatcher = provider.GetRequiredService<SagaDispatcher>();

        // Act
        var thrown = await CaptureAsync(() => dispatcher.DispatchAsync(
            new OrderPlaced(string.Empty),
            typeof(OrderPlaced),
            TestContext!.CancellationToken)).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<SagaCorrelationException>());
    }

    [TestMethod]
    public async Task InMemoryStore_RejectsAStaleVersionAsync() {
        // Arrange
        var serializer = new JsonMessageSerializer(Options.Create(new JsonMessageSerializerOptions()));
        var store = new InMemorySagaStore(serializer);
        var state = new OrderSagaState { CurrentState = "AwaitingPayment", OrderId = "stale" };
        await store.SaveAsync("stale", typeof(OrderSagaState), state, 0, SagaProgress.Empty, TestContext!.CancellationToken).ConfigureAwait(false);
        var loaded = await store.LoadAsync("stale", typeof(OrderSagaState), TestContext!.CancellationToken).ConfigureAwait(false);

        // Act: two writers use the same expected version; the first wins, the second is stale.
        loaded!.State.CurrentState = "Completed";
        await store.SaveAsync("stale", typeof(OrderSagaState), loaded.State, loaded.Version, SagaProgress.Empty, TestContext!.CancellationToken).ConfigureAwait(false);
        var thrown = await CaptureAsync(() => store.SaveAsync(
            "stale",
            typeof(OrderSagaState),
            new OrderSagaState { CurrentState = "Completed" },
            loaded.Version,
            SagaProgress.Empty,
            TestContext!.CancellationToken)).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<SagaConcurrencyException>());
        MSTestAssertions.That(() => ((SagaConcurrencyException)thrown!).ActualVersion.Should().Be(2));
    }

    [TestMethod]
    public void AddCqrsSagas_RegistersOneObserverAndTheMachine() {
        // Arrange
        var (provider, _, _) = Build();

        // Act
        var observers = provider.GetServices<INotificationObserver>().ToList();
        var machines = provider.GetServices<ISaga>().ToList();
        var hosted = provider.GetServices<IHostedService>().ToList();

        provider.Dispose();

        // Assert
        MSTestAssertions.That(() => observers.Should().HaveCount(1));
        MSTestAssertions.That(() => machines.Should().HaveCount(1));
        MSTestAssertions.That(() => hosted.Should().NotBeEmpty());
    }

    private static (ServiceProvider Provider, ISagaStore Store, List<string> Log) Build(ISagaStore? store = null, List<string>? log = null) {
        log ??= [];
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddTransient<ICommandHandler<ReserveStock>, ReserveStockHandler>();
        services.AddTransient<ICommandHandler<ShipOrder>, ShipOrderHandler>();

        if(store is not null) {
            services.AddSingleton(store);
        }

        services.AddCqrsSagas<OrderSaga>();
        var provider = services.BuildServiceProvider();

        return (provider, provider.GetRequiredService<ISagaStore>(), log);
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
