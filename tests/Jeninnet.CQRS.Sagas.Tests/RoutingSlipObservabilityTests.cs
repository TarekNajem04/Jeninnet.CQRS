//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//

namespace Jeninnet.CQRS.Sagas.Tests;

/// <summary>
/// CQRS-58 metrics: a completed slip emits one completed counter and one step counter per step,
/// dimensioned by the definition name.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RoutingSlipObservabilityTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task CompletedSlip_EmitsStepAndCompletedCountersAsync() {
        // Arrange
        var longs = new Dictionary<string, long>(StringComparer.Ordinal);
        const string definition = "observed-slip";
        using var listener = new MeterListener {
            InstrumentPublished = static (instrument, current) => {
                if(instrument.Meter.Name == RoutingSlipDiagnosticNames.MeterName &&
                   Array.IndexOf(_instruments, instrument.Name) >= 0) {
                    current.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => {
            if(HasDefinition(tags, definition)) {
                lock(longs) {
                    longs[instrument.Name] = value;
                }
            }
        });
        listener.Start();

        var services = new ServiceCollection();
        services.AddRoutingSlip<ObservedArguments>(
            slip => slip.AddStep<NoopActivity>("noop"),
            definition);
        await using var provider = services.BuildServiceProvider();

        // Act
        var result = await provider.GetRequiredService<RoutingSlipExecutor>()
            .ExecuteAsync(new ObservedArguments(), "slip-1", TestContext!.CancellationToken)
            .ConfigureAwait(false);
        listener.RecordObservableInstruments();

        // Assert
        MSTestAssertions.That(() => result.Status.Should().Be(RoutingSlipStatus.Completed));
        MSTestAssertions.That(() => longs[RoutingSlipDiagnosticNames.Completed].Should().Be(1));
        MSTestAssertions.That(() => longs[RoutingSlipDiagnosticNames.Step].Should().Be(1));
        MSTestAssertions.That(() => longs.ContainsKey(RoutingSlipDiagnosticNames.Compensated).Should().BeFalse());
        MSTestAssertions.That(() => longs.ContainsKey(RoutingSlipDiagnosticNames.Faulted).Should().BeFalse());
    }

    private static readonly string[] _instruments = [
        RoutingSlipDiagnosticNames.Completed,
        RoutingSlipDiagnosticNames.Compensated,
        RoutingSlipDiagnosticNames.Faulted,
        RoutingSlipDiagnosticNames.Step,
    ];

    private static bool HasDefinition(ReadOnlySpan<KeyValuePair<string, object?>> tags, string definition) {
        foreach(var tag in tags) {
            if(tag.Key == RoutingSlipDiagnosticNames.DefinitionTag &&
               string.Equals(tag.Value as string, definition, StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    public sealed class ObservedArguments {
        public string? Value { get; set; }
    }

    public sealed class NoopActivity : IActivity<ObservedArguments> {
        public Task ExecuteAsync(ObservedArguments arguments, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CompensateAsync(ObservedArguments arguments, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
