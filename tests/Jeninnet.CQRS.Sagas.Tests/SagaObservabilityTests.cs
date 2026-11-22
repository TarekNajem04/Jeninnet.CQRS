//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.Tests;

/// <summary>
/// Characterization tests for CQRS-49 observability: one saga span per event and one completed
/// counter measurement per terminal transition, with the active gauge reading the store.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class SagaObservabilityTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task TwoEvents_EmitOneSpanEach_AndOneCompletedMeasurementAsync() {
        // Arrange
        var stopped = new List<Activity>();
        var longs = new Dictionary<string, long>(StringComparer.Ordinal);
        var doubles = new Dictionary<string, double>(StringComparer.Ordinal);
        using var activityListener = new ActivityListener {
            ShouldListenTo = static source => source.Name == SagaDiagnosticNames.ActivitySourceName,
            Sample = static (ref _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => {
                lock(stopped) {
                    stopped.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(activityListener);
        using var meterListener = new MeterListener {
            InstrumentPublished = static (instrument, listener) => {
                if(instrument.Meter.Name == SagaDiagnosticNames.MeterName &&
                   Array.IndexOf(_instrumentNames, instrument.Name) >= 0) {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, _, _) => longs[instrument.Name] = value);
        meterListener.SetMeasurementEventCallback<double>((instrument, value, _, _) => doubles[instrument.Name] = value);
        meterListener.Start();

        var services = new ServiceCollection();
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddTransient<ICommandHandler<ReserveStock>, NoopReserveHandler>();
        services.AddTransient<ICommandHandler<ShipOrder>, NoopShipHandler>();
        services.AddCqrsSagas<OrderSaga>();
        await using var provider = services.BuildServiceProvider();
        var broker = provider.GetRequiredService<ICqrsBroker>();

        // Act
        await broker.PublishAsync(new OrderPlaced("observed-1"), TestContext!.CancellationToken).ConfigureAwait(false);
        await broker.PublishAsync(new PaymentTaken("observed-1", "payment-1"), TestContext!.CancellationToken).ConfigureAwait(false);
        meterListener.RecordObservableInstruments();

        // Assert: two spans, one per event, with the saga id and state type.
        List<Activity> spans;
        lock(stopped) {
            spans = [.. stopped.Where(activity => activity.OperationName == SagaDiagnosticNames.StepActivity)];
        }

        MSTestAssertions.That(() => spans.Should().HaveCount(2));
        MSTestAssertions.That(() => spans.Any(activity => TagOf(activity, SagaDiagnosticNames.SagaIdTag) == "observed-1").Should().BeTrue());
        MSTestAssertions.That(() => spans.Any(activity => TagOf(activity, SagaDiagnosticNames.StateTypeTag) == nameof(OrderSagaState)).Should().BeTrue());
        MSTestAssertions.That(() => longs[SagaDiagnosticNames.Completed].Should().Be(1));
        MSTestAssertions.That(() => longs[SagaDiagnosticNames.Active].Should().Be(1));
        MSTestAssertions.That(() => doubles.ContainsKey(SagaDiagnosticNames.Active).Should().BeFalse("the active instrument is a long gauge"));
    }

    private static readonly string[] _instrumentNames = [
        SagaDiagnosticNames.Active,
        SagaDiagnosticNames.Completed,
        SagaDiagnosticNames.Faulted,
    ];

    private static string? TagOf(Activity activity, string key) =>
        activity.TagObjects.FirstOrDefault(tag => tag.Key == key).Value as string;

    private sealed class NoopReserveHandler : ICommandHandler<ReserveStock> {
        public Task<Unit> HandleAsync(ReserveStock command, CancellationToken cancellationToken) =>
            Task.FromResult(Unit.Value);
    }

    private sealed class NoopShipHandler : ICommandHandler<ShipOrder> {
        public Task<Unit> HandleAsync(ShipOrder command, CancellationToken cancellationToken) =>
            Task.FromResult(Unit.Value);
    }
}
