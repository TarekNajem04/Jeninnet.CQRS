//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

// Listener-based tests observe the process-wide ActivitySource/Meter, so they must not
// overlap: determinism beats parallelism here.
[DoNotParallelize]
[TestClass]
public class CqrsDiagnosticsTests {
    private static Task<string> Succeeding(TestCommand _, CancellationToken __) =>
        Task.FromResult("ok");

    private static Task<string> Failing(TestCommand _, CancellationToken __) =>
        Task.FromException<string>(new InvalidOperationException("boom"));

    private static Task<string> Cancelling(TestCommand _, CancellationToken __) =>
        Task.FromException<string>(new OperationCanceledException());

    private static DiagnosticsBehavior<TestCommand, string> Behavior(bool tracing = true, bool metrics = true) =>
        new(Options.Create(new CqrsDiagnosticsOptions { EnableTracing = tracing, EnableMetrics = metrics }));

    private static ActivityListener CreateActivityListener(List<Activity> stopped) {
        var listener = new ActivityListener {
            ShouldListenTo = source => source.Name == CqrsDiagnosticNames.SOURCE_NAME,
            Sample = static (ref _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => stopped.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);

        return listener;
    }

    private sealed record Measurement(string Instrument, double DoubleValue, long LongValue, bool IsDouble, KeyValuePair<string, object?>[] Tags);

    private static MeterListener CreateMeterListener(List<Measurement> measurements) {
        var listener = new MeterListener {
            InstrumentPublished = (instrument, l) => {
                if(instrument.Meter.Name == CqrsDiagnosticNames.METER_NAME) {
                    l.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add(new Measurement(instrument.Name, value, 0, true, [.. tags])));
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add(new Measurement(instrument.Name, 0, value, false, [.. tags])));
        listener.Start();

        return listener;
    }

    private static string? TagOf(Activity activity, string key) =>
        activity.TagObjects.FirstOrDefault(t => t.Key == key).Value as string;

    private static string? TagOf(Measurement measurement, string key) =>
        measurement.Tags.FirstOrDefault(t => t.Key == key).Value as string;

    [TestMethod]
    public async Task Success_StartsSpan_WithRequestTags_AndOkStatusAsync() {
        // Arrange
        var stopped = new List<Activity>();
        var measurements = new List<Measurement>();
        using var _activityListener = CreateActivityListener(stopped);
        using var _meterListener = CreateMeterListener(measurements);
        var behavior = Behavior();

        // Act
        var result = await behavior.HandleAsync(new TestCommand("Run"), Succeeding, default);

        // Assert
        MSTestAssertions.That(() => result.Should().Be("ok"));
        MSTestAssertions.That(() => stopped.Should().HaveCount(1));
        MSTestAssertions.That(() => stopped[0].OperationName.Should().Be("Dispatch"));
        MSTestAssertions.That(() => TagOf(stopped[0], "cqrs.request").Should().Be(typeof(TestCommand).FullName));
        MSTestAssertions.That(() => stopped[0].Status.Should().Be(ActivityStatusCode.Ok));

        var durations = measurements.Where(m => m.Instrument == "dispatch.duration").ToList();
        MSTestAssertions.That(() => durations.Should().HaveCount(1));
        MSTestAssertions.That(() => durations[0].DoubleValue.Should().BeGreaterThanOrEqualTo(0));
        MSTestAssertions.That(() => TagOf(durations[0], "cqrs.outcome").Should().Be("success"));
        MSTestAssertions.That(() => measurements.Any(m => m.Instrument == "dispatch.errors").Should().BeFalse());
    }

    [TestMethod]
    public async Task DisabledBehavior_ProducesNothingAsync() {
        // Arrange
        var stopped = new List<Activity>();
        var measurements = new List<Measurement>();
        using var _activityListener = CreateActivityListener(stopped);
        using var _meterListener = CreateMeterListener(measurements);
        var behavior = Behavior(tracing: false, metrics: false);

        // Act
        var result = await behavior.HandleAsync(new TestCommand("Run"), Succeeding, default);

        // Assert: the single-await fast path emits nothing.
        MSTestAssertions.That(() => result.Should().Be("ok"));
        MSTestAssertions.That(() => stopped.Should().BeEmpty());
        MSTestAssertions.That(() => measurements.Should().BeEmpty());
    }

    [TestMethod]
    public async Task Failure_RecordsError_AndErrorStatusAsync() {
        // Arrange
        var stopped = new List<Activity>();
        var measurements = new List<Measurement>();
        using var _activityListener = CreateActivityListener(stopped);
        using var _meterListener = CreateMeterListener(measurements);
        var behavior = Behavior();

        // Act
        InvalidOperationException? thrown = null;
        try {
            await behavior.HandleAsync(new TestCommand("Run"), Failing, default);
        }
        catch(InvalidOperationException ex) {
            thrown = ex;
        }

        // Assert: the original error propagates and is counted once with its type.
        MSTestAssertions.That(() => thrown.Should().NotBeNull());
        MSTestAssertions.That(() => thrown!.Message.Should().Be("boom"));
        MSTestAssertions.That(() => stopped.Should().HaveCount(1));
        MSTestAssertions.That(() => stopped[0].Status.Should().Be(ActivityStatusCode.Error));

        var errors = measurements.Where(m => m.Instrument == "dispatch.errors").ToList();
        MSTestAssertions.That(() => errors.Should().HaveCount(1));
        MSTestAssertions.That(() => TagOf(errors[0], "cqrs.exception")!.Should().Contain("InvalidOperationException"));
        MSTestAssertions.That(() => TagOf(errors[0], "cqrs.outcome").Should().Be("failure"));
    }

    [TestMethod]
    public async Task Cancellation_RecordsCancelled_WithoutErrorCountAsync() {
        // Arrange
        var stopped = new List<Activity>();
        var measurements = new List<Measurement>();
        using var _activityListener = CreateActivityListener(stopped);
        using var _meterListener = CreateMeterListener(measurements);
        var behavior = Behavior();

        // Act
        OperationCanceledException? thrown = null;
        try {
            await behavior.HandleAsync(new TestCommand("Run"), Cancelling, default);
        }
        catch(OperationCanceledException ex) {
            thrown = ex;
        }

        // Assert: cancellation is an outcome, not a failure.
        MSTestAssertions.That(() => thrown.Should().NotBeNull());
        MSTestAssertions.That(() => stopped.Should().HaveCount(1));
        MSTestAssertions.That(() => stopped[0].Status.Should().Be(ActivityStatusCode.Unset));
        MSTestAssertions.That(() => TagOf(measurements.Single(m => m.Instrument == "dispatch.duration"), "cqrs.outcome").Should().Be("cancelled"));
        MSTestAssertions.That(() => measurements.Any(m => m.Instrument == "dispatch.errors").Should().BeFalse());
    }

    [TestMethod]
    public void Registration_ResolvesBehavior_AsOutermost() {
        // Arrange
        var services = new ServiceCollection();
        services.AddCqrsDiagnostics();
        var provider = services.BuildServiceProvider();

        // Act
        var behavior = provider.GetServices<IPipelineBehavior<TestCommand, string>>().OfType<DiagnosticsBehavior<TestCommand, string>>().Single();
        var order = ((IOrderedPipelineBehavior<TestCommand, string>)behavior).Order;

        // Assert
        MSTestAssertions.That(() => order.Should().Be(PipelineOrder.DIAGNOSTICS));
        MSTestAssertions.That(() => PipelineOrder.DIAGNOSTICS.Should().BeLessThan(PipelineOrder.ERROR_HANDLING));
    }

    private sealed record TestCommand(string Message) : ICommand<string>;
}