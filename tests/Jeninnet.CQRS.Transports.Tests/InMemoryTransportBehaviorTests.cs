//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports.Tests;

/// <summary>
/// In-memory-specific behavior and the transport-selection contract: default remains
/// in-process, registration selects in-memory, naming is stable, and the core assembly stays
/// broker-free.
/// </summary>
[TestClass]
public class InMemoryTransportBehaviorTests {
    [TestMethod]
    public void DefaultTransportMode_IsInProcess() {
        // Arrange / Act
        var options = new CqrsTransportOptions();

        // Assert
        MSTestAssertions.That(() => options.Mode.Should().Be(CqrsTransportMode.InProcess));
    }

    [TestMethod]
    public void AddCqrsInMemoryTransport_RegistersTheTransportAndSelectsInMemory() {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCqrsInMemoryTransport();
        using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IMessageTransport>();
        var options = provider.GetRequiredService<IOptions<CqrsTransportOptions>>().Value;

        // Assert
        MSTestAssertions.That(() => transport.Should().BeOfType<InMemoryTransport>());
        MSTestAssertions.That(() => options.Mode.Should().Be(CqrsTransportMode.InMemory));
    }

    [TestMethod]
    public void NamingConvention_ProducesStableBrokerSafeNames() {
        // Arrange
        var convention = new TransportNamingConvention("acme");

        // Act
        var topology = convention.Describe(typeof(TransportContractMessage));

        // Assert
        MSTestAssertions.That(() => topology.Exchange.Should().StartWith("acme."));
        MSTestAssertions.That(() => topology.Exchange.Should().Contain(nameof(TransportContractMessage)));
        MSTestAssertions.That(() => topology.Queue.Should().Contain(".queue"));
        MSTestAssertions.That(() => topology.Topic.Should().Be(topology.Exchange));
        MSTestAssertions.That(() => topology.RoutingKey.Should().Contain(nameof(TransportContractMessage)));
    }

    [TestMethod]
    public async Task Timestamp_UsesTheInjectedClockAsync() {
        // Arrange
        var now = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var probe = new TransportProbe();
        IMessageTransport transport = new InMemoryTransport(CreateSerializer(), [probe], new TransportNamingConvention(), new ManualTimeProvider(now));
        await transport.StartAsync().ConfigureAwait(false);

        try {
            // Act
            await transport.SendAsync(new TransportContractMessage("clock", 1)).ConfigureAwait(false);

            // Assert
            MSTestAssertions.That(() => probe.Received.Single().Timestamp.Should().Be(now));
        }
        finally {
            await transport.StopAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task SendBeforeStart_ThrowsAsync() {
        // Arrange
        IMessageTransport transport = new InMemoryTransport(CreateSerializer(), [], new TransportNamingConvention());

        // Act
        var thrown = await CaptureAsync(() => transport.SendAsync(new TransportContractMessage("early", 0))).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<InvalidOperationException>());
    }

    [TestMethod]
    public void CoreAssembly_DoesNotReferenceBrokerClients() {
        // Arrange
        string[] brokerPrefixes = ["RabbitMQ", "Confluent", "Kafka", "Azure.Messaging", "AWSSDK", "Amazon", "MassTransit", "Wolverine", "Brighter"];

        // Act: a broker type in the core assembly would appear in its reference list.
        var brokerReferences = typeof(IMessageTransport).Assembly
            .GetReferencedAssemblies()
            .Select(static assembly => assembly.Name ?? string.Empty)
            .Where(name => brokerPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Assert
        MSTestAssertions.That(() => brokerReferences.Should().BeEmpty());
    }

    private static JsonMessageSerializer CreateSerializer() =>
        new(Options.Create(new JsonMessageSerializerOptions()));

    private static async Task<Exception?> CaptureAsync(Func<Task> action) {
        try {
            await action().ConfigureAwait(false);

            return null;
        }
        catch(Exception exception) {
            return exception;
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            throw new NotSupportedException("The in-memory transport only reads the current time.");
    }
}