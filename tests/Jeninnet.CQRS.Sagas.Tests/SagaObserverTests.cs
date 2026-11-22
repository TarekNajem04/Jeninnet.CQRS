//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.Tests;

/// <summary>
/// CQRS-60 saga observers: one pre-event and one post-transition observation per dispatched event,
/// including ignored events, in registration order.
/// </summary>
[TestClass]
public sealed class SagaObserverTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task SagaEvents_FirePreAndPostObservers_InOrderAsync() {
        // Arrange
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddTransient<ICommandHandler<ReserveStock>, ReserveStockHandler>();
        services.AddTransient<ICommandHandler<ShipOrder>, ShipOrderHandler>();
        services.AddCqrsSagas<OrderSaga>();
        services.AddCqrsSagaObserver<RecordingSagaObserver>();
        await using var provider = services.BuildServiceProvider();
        var broker = provider.GetRequiredService<ICqrsBroker>();

        // Act: a new saga and an event with no matching saga.
        await broker.PublishAsync(new OrderPlaced("obs-1"), TestContext!.CancellationToken).ConfigureAwait(false);
        await broker.PublishAsync(new PaymentTaken("missing", "payment-1"), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => log.Should().Equal([
            "event:obs-1:OrderPlaced",
            "reserve:obs-1",
            "transition:Created:AwaitingPayment",
            "event:missing:PaymentTaken",
            "transition:Ignored:",
        ]));
    }

    public sealed class RecordingSagaObserver(List<string> log) : ISagaObserver {
        private readonly List<string> _log = log ?? throw new ArgumentNullException(nameof(log));

        public Task OnSagaEventAsync(SagaObservationContext context, CancellationToken cancellationToken = default) {
            _log.Add($"event:{context.SagaId}:{context.MessageType.Name}");

            return Task.CompletedTask;
        }

        public Task OnSagaTransitionAsync(SagaObservationContext context, CancellationToken cancellationToken = default) {
            _log.Add($"transition:{context.Status}:{context.State}");

            return Task.CompletedTask;
        }

        public Task OnSagaFaultAsync(SagaObservationContext context, Exception error, CancellationToken cancellationToken = default) {
            _log.Add($"fault:{context.SagaId}");

            return Task.CompletedTask;
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
