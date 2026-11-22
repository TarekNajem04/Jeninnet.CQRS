//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// Characterization tests for CQRS-31 gauges: real store values with a store-type tag, and no
/// instruments at all when no durability store is registered.
/// </summary>
[TestClass]
[DoNotParallelize]
public class DurabilityObservabilityTests {
    public TestContext? TestContext { get; set; }

    private static readonly string[] _instrumentNames = [
        CqrsDiagnosticNames.DEFERRED_PENDING,
        CqrsDiagnosticNames.DEFERRED_OLDEST_DUE_SECONDS,
        CqrsDiagnosticNames.OUTBOX_PENDING,
        CqrsDiagnosticNames.DEADLETTER_COUNT,
    ];

    [TestMethod]
    public async Task Gauges_ReportSeededStoreValues_WithStoreTagsAsync() {
        // Arrange
        var clock = new ManualClock();
        var deferred = new InMemoryDeferredStore();
        var outbox = new InMemoryOutboxStore();
        var deadLetters = new InMemoryDeadLetterStore(Options.Create(new DeadLetterOptions()));
        var services = new ServiceCollection();
        services.AddSingleton<IDeferredStore>(deferred);
        services.AddSingleton<IOutboxStore>(outbox);
        services.AddSingleton<IDeadLetterStore>(deadLetters);
        services.AddSingleton<TimeProvider>(clock);
        services.AddCqrsDurabilityObservability();
        await using var provider = services.BuildServiceProvider();

        await deferred.StageAsync(new DeferredEnvelope<GaugeCommand>(new GaugeCommand("old"), clock.GetUtcNow().AddSeconds(-10), null), TestContext!.CancellationToken);
        await deferred.StageAsync(new DeferredEnvelope<GaugeCommand>(new GaugeCommand("new"), clock.GetUtcNow().AddSeconds(-5), null), TestContext!.CancellationToken);
        await outbox.StageAsync(new OutboxNotification<GaugeNotification>(new GaugeNotification("n"), clock.GetUtcNow().AddSeconds(-30)), TestContext!.CancellationToken);
        await deadLetters.RecordAsync(
            new DeferredEnvelope<GaugeCommand>(new GaugeCommand("failed"), clock.GetUtcNow(), null),
            new InvalidOperationException("boom"),
            clock.GetUtcNow(),
            TestContext!.CancellationToken);

        var longs = new Dictionary<string, long>(StringComparer.Ordinal);
        var doubles = new Dictionary<string, double>(StringComparer.Ordinal);
        var stores = new Dictionary<string, string?>(StringComparer.Ordinal);
        using var listener = new MeterListener {
            InstrumentPublished = static (instrument, meterListener) => {
                if(instrument.Meter.Name == CqrsDiagnosticNames.METER_NAME && Array.IndexOf(_instrumentNames, instrument.Name) >= 0) {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => {
            longs[instrument.Name] = value;
            stores[instrument.Name] = StoreTag(tags);
        });
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => {
            doubles[instrument.Name] = value;
            stores[instrument.Name] = StoreTag(tags);
        });
        listener.Start();

        foreach(var hosted in provider.GetServices<IHostedService>()) {
            await hosted.StartAsync(TestContext!.CancellationToken);
        }

        // Act
        listener.RecordObservableInstruments();

        // Assert
        MSTestAssertions.That(() => longs[CqrsDiagnosticNames.DEFERRED_PENDING].Should().Be(2));
        MSTestAssertions.That(() => doubles[CqrsDiagnosticNames.DEFERRED_OLDEST_DUE_SECONDS].Should().Be(10));
        MSTestAssertions.That(() => longs[CqrsDiagnosticNames.OUTBOX_PENDING].Should().Be(1));
        MSTestAssertions.That(() => longs[CqrsDiagnosticNames.DEADLETTER_COUNT].Should().Be(1));

        MSTestAssertions.That(() => stores[CqrsDiagnosticNames.DEFERRED_PENDING].Should().Be(nameof(InMemoryDeferredStore)));
        MSTestAssertions.That(() => stores[CqrsDiagnosticNames.OUTBOX_PENDING].Should().Be(nameof(InMemoryOutboxStore)));
        MSTestAssertions.That(() => stores[CqrsDiagnosticNames.DEADLETTER_COUNT].Should().Be(nameof(InMemoryDeadLetterStore)));
    }

    [TestMethod]
    public async Task AbsentStores_RegisterNoInstrumentsAsync() {
        // Arrange: observability without any durability store.
        var services = new ServiceCollection();
        services.AddCqrsDurabilityObservability();
        await using var provider = services.BuildServiceProvider();

        var observed = new List<string>();
        using var listener = new MeterListener {
            InstrumentPublished = static (instrument, meterListener) => {
                if(instrument.Meter.Name == CqrsDiagnosticNames.METER_NAME && Array.IndexOf(_instrumentNames, instrument.Name) >= 0) {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => observed.Add(instrument.Name));
        listener.SetMeasurementEventCallback<double>((instrument, _, _, _) => observed.Add(instrument.Name));
        listener.Start();

        foreach(var hosted in provider.GetServices<IHostedService>()) {
            await hosted.StartAsync(TestContext!.CancellationToken);
        }

        // Act
        listener.RecordObservableInstruments();

        // Assert
        MSTestAssertions.That(() => observed.Should().BeEmpty());
    }

    private static string? StoreTag(ReadOnlySpan<KeyValuePair<string, object?>> tags) {
        foreach(var tag in tags) {
            if(tag.Key == CqrsDiagnosticNames.STORE_TAG) {
                return tag.Value as string;
            }
        }

        return null;
    }

    private sealed record GaugeCommand(string Name) : ICommand;

    private sealed record GaugeNotification(string Name) : INotification;

    private sealed class ManualClock : TimeProvider {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
