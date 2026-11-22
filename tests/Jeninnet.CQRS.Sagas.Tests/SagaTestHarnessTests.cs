//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.Tests;

/// <summary>
/// CQRS-61 harness sample: a two-event saga transition and its published commands are asserted in
/// a few lines without a host, handlers, or container setup.
/// </summary>
[TestClass]
public sealed class SagaTestHarnessTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task Harness_AssertsTransitionAndPublishedCommandsAsync() {
        // Arrange
        using var harness = new SagaTestHarness<OrderSagaState, OrderPlaced>(new OrderSaga());

        // Act: the first event creates the saga.
        var created = await harness.When(new OrderPlaced("harness-1"), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        created.ShouldHaveTransitionedTo("AwaitingPayment");
        created.ShouldHavePublished<ReserveStock>(message => message.OrderId == "harness-1");
        MSTestAssertions.That(() => created.State.Should().NotBeNull());
        MSTestAssertions.That(() => created.State!.OrderId.Should().Be("harness-1"));

        // Act: the second event completes it.
        var completed = await harness.WhenAsync(
            new PaymentTaken("harness-1", "payment-1"),
            typeof(PaymentTaken),
            TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        completed.ShouldHaveTransitionedTo("Completed");
        completed.ShouldHavePublished<ShipOrder>(message => message.OrderId == "harness-1");
        completed.ShouldNotHavePublished<ReserveStock>();
    }
}