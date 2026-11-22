//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports.Tests;

/// <summary>
/// CQRS-63 rider model: a message reaches only its routed transport, incomplete routes fail at
/// startup, the host starts in registration order, stops in reverse, and emits per-rider metrics.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class RiderTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task Message_ReachesOnlyItsRoutedRiderAsync() {
        // Arrange
        var probeA = new TransportProbe();
        var probeB = new TransportProbe();
        var services = new ServiceCollection();
        services.AddCqrsRider("a", new InMemoryTransport(CreateSerializer(), [probeA], new TransportNamingConvention("a")));
        services.AddCqrsRider("b", new InMemoryTransport(CreateSerializer(), [probeB], new TransportNamingConvention("b")));
        services.Route<RiderMessageA>().To("a");
        services.Route<RiderMessageB>().To("b");

        using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<CqrsRiderHost>();
        var router = provider.GetRequiredService<IMessageTransport>();
        await host.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            // Act
            await router.SendAsync(new RiderMessageA("a-only"), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
            await router.SendAsync(new RiderMessageB("b-only"), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

            // Assert
            MSTestAssertions.That(() => probeA.Received.Should().HaveCount(1));
            MSTestAssertions.That(() => probeA.Received[0].MessageType.Should().Contain(nameof(RiderMessageA)));
            MSTestAssertions.That(() => probeB.Received.Should().HaveCount(1));
            MSTestAssertions.That(() => probeB.Received[0].MessageType.Should().Contain(nameof(RiderMessageB)));
        }
        finally {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task IncompleteRoute_FailsStartupWithTheMessageTypeAsync() {
        // Arrange
        var services = new ServiceCollection();
        services.AddCqrsRider("a", new LoggingTransport("a", []));
        services.Route<UnroutedMessage>();

        using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<CqrsRiderHost>();

        // Act
        var thrown = await CaptureAsync(() => host.StartAsync(TestContext!.CancellationToken)).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<CqrsRiderConfigurationException>());
        MSTestAssertions.That(() => thrown!.Message.Should().Contain(typeof(UnroutedMessage).FullName!));
    }

    [TestMethod]
    public async Task ConfiguredEndpointWithoutRoute_FailsStartupAsync() {
        // Arrange
        var services = new ServiceCollection();
        services.AddCqrsRider("a", new LoggingTransport("a", []));
        services.ConfigureEndpoint<EndpointOnlyMessage>(endpoint => endpoint.Named("endpoint-only"));

        using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<CqrsRiderHost>();

        // Act
        var thrown = await CaptureAsync(() => host.StartAsync(TestContext!.CancellationToken)).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<CqrsRiderConfigurationException>());
        MSTestAssertions.That(() => thrown!.Message.Should().Contain(typeof(EndpointOnlyMessage).FullName!));
    }

    [TestMethod]
    public async Task Host_StartsInRegistrationOrder_StopsInReverse_AndTagsMetricsAsync() {
        // Arrange
        var log = new List<string>();
        var observed = new List<(string Instrument, string? Rider)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = static (instrument, current) => {
            if(instrument.Meter.Name == CqrsRiderDiagnosticNames.MeterName) {
                current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => {
            string? rider = null;

            foreach(var tag in tags) {
                if(tag.Key == CqrsRiderDiagnosticNames.RiderTag) {
                    rider = tag.Value?.ToString();
                    break;
                }
            }

            observed.Add((instrument.Name, rider));
        });
        listener.Start();

        var services = new ServiceCollection();
        services.AddCqrsRider("a", new LoggingTransport("a", log));
        services.AddCqrsRider("b", new LoggingTransport("b", log));

        using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<CqrsRiderHost>();
        var registry = provider.GetRequiredService<CqrsRiderRegistry>();

        // Act
        await host.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        var started = registry.Snapshot();
        await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        var stopped = registry.Snapshot();

        // Assert
        MSTestAssertions.That(() => log.Should().Equal("start:a", "start:b", "stop:b", "stop:a"));
        MSTestAssertions.That(() => started.Select(static rider => rider.Status).Should().OnlyContain(status => status == CqrsRiderStatus.Started));
        MSTestAssertions.That(() => stopped.Select(static rider => rider.Status).Should().OnlyContain(status => status == CqrsRiderStatus.Stopped));
        MSTestAssertions.That(() => observed.Where(static item => item.Instrument == CqrsRiderDiagnosticNames.Started).Select(static item => item.Rider ?? string.Empty).Should().Equal("a", "b"));
        MSTestAssertions.That(() => observed.Where(static item => item.Instrument == CqrsRiderDiagnosticNames.Stopped).Select(static item => item.Rider ?? string.Empty).Should().Equal("b", "a"));
    }

    private static IMessageSerializer CreateSerializer() =>
        new JsonMessageSerializer(Options.Create(new JsonMessageSerializerOptions()));

    private static async Task<Exception?> CaptureAsync(Func<Task> action) {
        try {
            await action().ConfigureAwait(false);

            return null;
        }
        catch(Exception exception) {
            return exception;
        }
    }

    private sealed record RiderMessageA(string Value);

    private sealed record RiderMessageB(string Value);

    private sealed record UnroutedMessage(string Value);

    private sealed record EndpointOnlyMessage(string Value);

    private sealed class LoggingTransport(string name, List<string> log) : IMessageTransport {
        public Task SendAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task PublishAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken = default) {
            lock(log) {
                log.Add($"start:{name}");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default) {
            lock(log) {
                log.Add($"stop:{name}");
            }

            return Task.CompletedTask;
        }
    }
}