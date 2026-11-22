//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//

namespace Jeninnet.CQRS.Transport.AzureServiceBus.Tests;

/// <summary>
/// Azure Service Bus adapter tests: shared contract, session ordering, native scheduling, and
/// dead-letter forwarding with the original headers. The emulator topology is provisioned through
/// Config.json because its HTTP management endpoint is not exposed by this container wiring;
/// production keeps <see cref="AzureServiceBusTransportOptions.EnsureTopology"/> enabled.
/// Container tests become inconclusive when Docker is unavailable.
/// </summary>
[TestClass]
public sealed class AzureServiceBusTransportTests : TransportContractTests {
    private const string _fallbackPrefix = "t99";
    private const string _sessionPrefix = "s01";
    private static readonly JsonSerializerOptions _configJson = new() { WriteIndented = true };
    private static readonly Dictionary<string, string> _prefixes = new(StringComparer.Ordinal) {
        [nameof(TransportContractTests.Send_RoundTripsTheMessageAndPreservesEnvelopeFieldsAsync)] = "t01",
        [nameof(TransportContractTests.Publish_ReachesEveryConsumerAsync)] = "t02",
        [nameof(TransportContractTests.Send_ReachesOnlyOneConsumerAsync)] = "t03",
        [nameof(TransportContractTests.Stop_RejectsNewMessagesAsync)] = "t04",
        [nameof(TransportContractTests.StartAndStop_AreIdempotentAsync)] = "t05",
        [nameof(ScheduledMessage_IsNotDeliveredEarlyAsync)] = "t06",
        [nameof(HandlerFailure_ForwardsTheOriginalEnvelopeToTheDeadLetterStoreAsync)] = "t07",
    };

    private static ServiceBusContainer? _container;
    private static string? _connectionString;
    private static string? _configPath;
    private static bool _dockerAvailable;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext) {
        if(!await DockerIsAvailableAsync(testContext.CancellationToken).ConfigureAwait(false)) {
            return;
        }

        _configPath = Path.Combine(Path.GetTempPath(), $"cqrs44-{Guid.NewGuid():N}.json");
        File.WriteAllText(_configPath, CreateEmulatorConfig());
        _container = new ServiceBusBuilder("mcr.microsoft.com/azure-messaging/servicebus-emulator:latest")
            .WithAcceptLicenseAgreement(true)
            .WithConfig(_configPath)
            .Build();
        await _container.StartAsync(testContext.CancellationToken).ConfigureAwait(false);
        _connectionString = _container.GetConnectionString();
        _dockerAvailable = true;
    }

    [ClassCleanup]
    public static async Task ClassCleanupAsync() {
        if(_container is not null) {
            await _container.DisposeAsync().ConfigureAwait(false);
        }

        if(_configPath is not null && File.Exists(_configPath)) {
            try {
                File.Delete(_configPath);
            }
            catch(IOException) {
                // A lingering Docker mount keeps the temp file; it is outside the repo.
            }
        }
    }

    /// <inheritdoc />
    protected override Task<IMessageTransport> CreateTransportAsync(IMessageSerializer serializer, params IMessageTransportHandler[] handlers) {
        RequireDocker();

        var options = CreateOptions();
        var deadLetters = new InMemoryDeadLetterStore(Options.Create(new DeadLetterOptions()));
        var prefix = CurrentPrefix();

        return Task.FromResult<IMessageTransport>(new AzureServiceBusTransport(
            Options.Create(options),
            serializer,
            handlers,
            new TransportNamingConvention(prefix),
            deadLetters));
    }

    [TestMethod]
    public async Task SessionEnabled_PreservesOrderPerSessionAsync() {
        // Arrange
        RequireDocker();
        var probe = new TransportProbe();
        var options = CreateOptions(sessionsEnabled: true);
        var deadLetters = new InMemoryDeadLetterStore(Options.Create(new DeadLetterOptions()));
        IMessageTransport transport = new AzureServiceBusTransport(Options.Create(options), CreateSerializer(), [probe], new TransportNamingConvention(_sessionPrefix), deadLetters);
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        const int count = 20;

        try {
            // Act: one session id serializes handling on the session receiver.
            for(var index = 0; index < count; index++) {
                await transport.PublishAsync(
                    new TransportContractMessage($"s{index}", index),
                    new TransportMessageOptions { PartitionKey = "session-1" },
                    TestContext.CancellationToken).ConfigureAwait(false);
            }

            await SpinUntilAsync(() => probe.Received.Count >= count, "session-ordered deliveries", TestContext.CancellationToken).ConfigureAwait(false);

            // Assert
            var names = probe.Received
                .Select(envelope => (TransportContractMessage?)CreateSerializer().Deserialize(envelope.Payload.Span, typeof(TransportContractMessage)))
                .Select(message => message!.Name)
                .ToList();
            MSTestAssertions.That(() => names.Should().Equal(Enumerable.Range(0, count).Select(index => $"s{index}")));
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task ScheduledMessage_IsNotDeliveredEarlyAsync() {
        // Arrange
        RequireDocker();
        var probe = new TransportProbe();
        var options = CreateOptions();
        var deadLetters = new InMemoryDeadLetterStore(Options.Create(new DeadLetterOptions()));
        IMessageTransport transport = new AzureServiceBusTransport(Options.Create(options), CreateSerializer(), [probe], new TransportNamingConvention(CurrentPrefix()), deadLetters);
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            // Act: native ScheduledEnqueueTime holds the message on the broker.
            await transport.PublishAsync(
                new TransportContractMessage("later", 1),
                new TransportMessageOptions { ScheduledEnqueueTimeUtc = DateTimeOffset.UtcNow.AddSeconds(3) },
                TestContext.CancellationToken).ConfigureAwait(false);
            await Task.Delay(500, TestContext.CancellationToken).ConfigureAwait(false);
            var early = probe.Received.Count;
            await SpinUntilAsync(() => probe.Received.Count >= 1, "scheduled delivery", TestContext.CancellationToken).ConfigureAwait(false);

            // Assert
            MSTestAssertions.That(() => early.Should().Be(0));
            MSTestAssertions.That(() => probe.Received.Count.Should().Be(1));
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task HandlerFailure_ForwardsTheOriginalEnvelopeToTheDeadLetterStoreAsync() {
        // Arrange
        RequireDocker();
        var options = CreateOptions();
        var deadLetters = new InMemoryDeadLetterStore(Options.Create(new DeadLetterOptions()));
        IMessageTransport transport = new AzureServiceBusTransport(Options.Create(options), CreateSerializer(), [new FailingHandler()], new TransportNamingConvention(CurrentPrefix()), deadLetters);
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            // Act
            await transport.PublishAsync(
                new TransportContractMessage("poison", 1),
                new TransportMessageOptions {
                    CorrelationId = "corr-1",
                    Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["trace"] = "abc-123" },
                },
                TestContext.CancellationToken).ConfigureAwait(false);

            DeadLetterEntry? entry = null;
            var deadline = Environment.TickCount64 + 20_000;

            while(Environment.TickCount64 < deadline && entry is null) {
                var entries = await deadLetters.ListAsync(TestContext.CancellationToken).ConfigureAwait(false);
                entry = entries.Count > 0 ? entries[0] : null;

                if(entry is null) {
                    await Task.Delay(100, TestContext.CancellationToken).ConfigureAwait(false);
                }
            }

            // Assert: the CQRS dead-letter copy carries the original headers and payload.
            MSTestAssertions.That(() => entry.Should().NotBeNull("a handler failure must be inspectable in the CQRS dead-letter store"));
            var envelope = (TransportEnvelope)entry!.Envelope.Command;
            var body = (TransportContractMessage?)CreateSerializer().Deserialize(envelope.Payload.Span, typeof(TransportContractMessage));
            MSTestAssertions.That(() => envelope.CorrelationId.Should().Be("corr-1"));
            MSTestAssertions.That(() => envelope.Headers["trace"].Should().Be("abc-123"));
            MSTestAssertions.That(() => body.Should().Be(new TransportContractMessage("poison", 1)));
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public void AddCqrsAzureServiceBus_RegistersTheTransportAndSelectsExternalMode() {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCqrsAzureServiceBus(static options => {
            options.ConnectionString = "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=not-a-real-key";
            options.Subscriptions.Add(new AzureServiceBusSubscription { MessageType = typeof(TransportContractMessage) });
        });
        using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IMessageTransport>();
        var mode = provider.GetRequiredService<IOptions<CqrsTransportOptions>>().Value.Mode;
        var hostedServices = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().ToList();

        // Assert
        MSTestAssertions.That(() => transport.Should().BeOfType<AzureServiceBusTransport>());
        MSTestAssertions.That(() => mode.Should().Be(CqrsTransportMode.External));
        MSTestAssertions.That(() => hostedServices.Should().NotBeEmpty("the transport must start and stop with the host"));
    }

    [TestMethod]
    public void MissingAuthentication_IsRejectedWhenTheTransportIsConstructed() {
        // Arrange
        var options = new AzureServiceBusTransportOptions();

        // Act
        var thrown = Capture(() => _ = new AzureServiceBusTransport(
            Options.Create(options),
            CreateSerializer(),
            [],
            new TransportNamingConvention(),
            new InMemoryDeadLetterStore(Options.Create(new DeadLetterOptions()))));

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<ArgumentException>());
    }

    private string CurrentPrefix() =>
        TestContext?.TestName is { } name && _prefixes.TryGetValue(name, out var prefix) ? prefix : _fallbackPrefix;

    private static AzureServiceBusTransportOptions CreateOptions(bool sessionsEnabled = false) {
        var options = new AzureServiceBusTransportOptions {
            ConnectionString = _connectionString ?? throw new InvalidOperationException("The Service Bus emulator is not available."),
            EnsureTopology = false,
            SubscriptionPrefix = "cqrs",
            SessionsEnabled = sessionsEnabled,
            ReceiveWaitTime = TimeSpan.FromMilliseconds(250),
            PrefetchCount = 0,
        };
        options.Subscriptions.Add(new AzureServiceBusSubscription { MessageType = typeof(TransportContractMessage) });

        return options;
    }

    private static string CreateEmulatorConfig() {
        static object Properties(bool requiresSession) => new {
            DeadLetteringOnMessageExpiration = false,
            DefaultMessageTimeToLive = "PT1H",
            DuplicateDetectionHistoryTimeWindow = "PT20S",
            ForwardDeadLetteredMessagesTo = "",
            ForwardTo = "",
            LockDuration = "PT5M",
            MaxDeliveryCount = 10,
            RequiresDuplicateDetection = false,
            RequiresSession = requiresSession,
        };

        static object Topic(string name, bool requiresSession) => new {
            Name = name,
            Properties = new {
                DefaultMessageTimeToLive = "PT1H",
                DuplicateDetectionHistoryTimeWindow = "PT20S",
                RequiresDuplicateDetection = false,
            },
            Subscriptions = new[] {
                new { Name = "cqrs", Properties = Properties(requiresSession) },
                new { Name = "cqrs.1", Properties = Properties(requiresSession) },
            },
        };

        var contractPrefixes = _prefixes.Values
            .Append(_fallbackPrefix)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var contractQueues = contractPrefixes
            .Select(prefix => {
                var topology = new TransportNamingConvention(prefix).Describe(typeof(TransportContractMessage));

                return new { Name = topology.Queue, Properties = Properties(false) };
            })
            .ToList();
        var contractTopics = contractPrefixes
            .Select(prefix => Topic(new TransportNamingConvention(prefix).Describe(typeof(TransportContractMessage)).Topic, false))
            .ToList();
        var session = new TransportNamingConvention(_sessionPrefix).Describe(typeof(TransportContractMessage));
        contractQueues.Add(new { Name = session.Queue, Properties = Properties(true) });
        contractTopics.Add(Topic(session.Topic, true));
        var config = new {
            UserConfig = new {
                Namespaces = new[] {
                    new {
                        Name = "sbemulatorns",
                        Queues = contractQueues.ToArray(),
                        Topics = contractTopics.ToArray(),
                    },
                },
                Logging = new { Type = "File" },
            },
        };

        return JsonSerializer.Serialize(config, _configJson);
    }

    private static JsonMessageSerializer CreateSerializer() =>
        new(Options.Create(new JsonMessageSerializerOptions()));

    private static void RequireDocker() {
        if(!_dockerAvailable || _container is null) {
            Assert.Inconclusive("Docker is not available; the Azure Service Bus integration tests were skipped.");
        }
    }

    private static Task<bool> DockerIsAvailableAsync(CancellationToken cancellationToken) =>
        DockerAvailability.IsAvailableAsync(cancellationToken);

    private static async Task SpinUntilAsync(Func<bool> condition, string what, CancellationToken cancellationToken, int timeoutMilliseconds = 30_000) {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;

        while(!condition()) {
            if(Environment.TickCount64 > deadline) {
                break;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        MSTestAssertions.That(() => condition().Should().BeTrue($"timed out waiting for: {what}"));
    }

    private static Exception? Capture(Action action) {
        try {
            action();

            return null;
        }
        catch(Exception exception) {
            return exception;
        }
    }

    private sealed class FailingHandler : IMessageTransportHandler {
        public Task HandleAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("poison message");
    }
}