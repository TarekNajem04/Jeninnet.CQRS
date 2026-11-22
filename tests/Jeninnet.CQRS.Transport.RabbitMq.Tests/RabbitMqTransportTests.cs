//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.RabbitMq.Tests;

/// <summary>
/// RabbitMQ adapter tests: the shared transport contract plus broker-specific proofs for
/// publisher confirms, handler-failure dead-lettering, and reconnect after a broker restart.
/// Container tests become inconclusive when Docker is unavailable; CI runs them for real.
/// </summary>
[TestClass]
public sealed class RabbitMqTransportTests : TransportContractTests {
    private static RabbitMqContainer? _container;
    private static Uri? _endpoint;
    private static bool _dockerAvailable;
    private static int _transportCounter;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext) {
        if(!await DockerIsAvailableAsync(testContext.CancellationToken).ConfigureAwait(false)) {
            return;
        }

        _container = new RabbitMqBuilder("rabbitmq:4.2-management").Build();
        await _container.StartAsync(testContext.CancellationToken).ConfigureAwait(false);
        _endpoint = new Uri(_container.GetConnectionString());
        _dockerAvailable = true;
    }

    [ClassCleanup]
    public static async Task ClassCleanupAsync() {
        if(_container is not null) {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    protected override Task<IMessageTransport> CreateTransportAsync(IMessageSerializer serializer, params IMessageTransportHandler[] handlers) {
        RequireDocker();

        var namingConvention = new TransportNamingConvention(UniquePrefix());

        return Task.FromResult<IMessageTransport>(new RabbitMqTransport(
            Options.Create(CreateOptions()),
            serializer,
            handlers,
            namingConvention));
    }

    [TestMethod]
    public async Task PublishToAnUnroutableSend_SurfacesTheConfirmFailureAsync() {
        // Arrange
        RequireDocker();
        var transport = await CreateTransportAsync(CreateSerializer(), new TransportProbe()).ConfigureAwait(false);
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            // Act: this message type has no declared queue, so mandatory + confirms must fail.
            var thrown = await CaptureAsync(() => transport.SendAsync(new UnsubscribedMessage("lost"), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

            // Assert
            MSTestAssertions.That(() => thrown.Should().NotBeNull("publisher confirms must reject unroutable messages"));
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task HandlerFailure_DeadLettersTheMessageAsync() {
        // Arrange
        RequireDocker();
        var namingConvention = new TransportNamingConvention(UniquePrefix());
        var options = CreateOptions();
        options.Subscriptions.Add(new RabbitMqSubscription { MessageType = typeof(TransportContractMessage) });
        var topology = namingConvention.Describe(typeof(TransportContractMessage));
        IMessageTransport transport = new RabbitMqTransport(Options.Create(options), CreateSerializer(), [new FailingHandler()], namingConvention);
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            // Act: the consumer rejects without requeue, so the queue's DLX moves it to the DLQ.
            await transport.PublishAsync(new TransportContractMessage("poison", 1), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

            await using var connection = await CreateConnectionFactory().CreateConnectionAsync(TestContext.CancellationToken).ConfigureAwait(false);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
            BasicGetResult? deadLettered = null;
            var deadline = Environment.TickCount64 + 30_000;

            while(Environment.TickCount64 < deadline && deadLettered is null) {
                deadLettered = await channel.BasicGetAsync($"{topology.Queue}.dlq", autoAck: true, TestContext.CancellationToken).ConfigureAwait(false);

                if(deadLettered is null) {
                    await Task.Delay(100, TestContext.CancellationToken).ConfigureAwait(false);
                }
            }

            // Assert
            MSTestAssertions.That(() => deadLettered.Should().NotBeNull("a rejected delivery must reach the dead-letter queue"));
            var body = (TransportContractMessage?)CreateSerializer().Deserialize(deadLettered!.Body.Span, typeof(TransportContractMessage));
            MSTestAssertions.That(() => body.Should().Be(new TransportContractMessage("poison", 1)));
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task BrokerRestart_ResumesConsumptionWithoutRestartingTheTransportAsync() {
        // Arrange
        RequireDocker();
        var probe = new TransportProbe();
        var transport = await CreateTransportAsync(CreateSerializer(), probe).ConfigureAwait(false);
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            await transport.SendAsync(new TransportContractMessage("before-restart", 1), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
            await SpinUntilAsync(() => probe.Received.Count >= 1, "initial delivery", TestContext.CancellationToken).ConfigureAwait(false);

            // Act: restart the broker application; the credentials/port stay valid.
            await _container!.ExecAsync(["rabbitmqctl", "stop_app"], TestContext.CancellationToken).ConfigureAwait(false);
            await Task.Delay(500, TestContext.CancellationToken).ConfigureAwait(false);
            await _container.ExecAsync(["rabbitmqctl", "start_app"], TestContext.CancellationToken).ConfigureAwait(false);

            var publishDeadline = Environment.TickCount64 + 30_000;

            while(Environment.TickCount64 < publishDeadline) {
                try {
                    await transport.SendAsync(new TransportContractMessage("after-restart", 2), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

                    break;
                }
                catch(InvalidOperationException) {
                    await Task.Delay(200, TestContext.CancellationToken).ConfigureAwait(false);
                }
                catch(RabbitMQ.Client.Exceptions.AlreadyClosedException) {
                    await Task.Delay(200, TestContext.CancellationToken).ConfigureAwait(false);
                }
                catch(RabbitMQ.Client.Exceptions.BrokerUnreachableException) {
                    await Task.Delay(200, TestContext.CancellationToken).ConfigureAwait(false);
                }
            }

            await SpinUntilAsync(() => probe.Received.Count >= 2, "delivery after broker restart", TestContext.CancellationToken).ConfigureAwait(false);

            // Assert: the same transport instance and consumer survived; no host restart.
            MSTestAssertions.That(() => probe.Received.Count.Should().Be(2));
            MSTestAssertions.That(() => probe.Received[1].Payload.Length.Should().BeGreaterThan(0));
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public void AddCqrsRabbitMq_RegistersTheTransportAndSelectsExternalMode() {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCqrsRabbitMq(static options => options.Subscriptions.Add(new RabbitMqSubscription { MessageType = typeof(TransportContractMessage) }));
        using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IMessageTransport>();
        var mode = provider.GetRequiredService<IOptions<CqrsTransportOptions>>().Value.Mode;
        var hostedServices = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().ToList();

        // Assert
        MSTestAssertions.That(() => transport.Should().BeOfType<RabbitMqTransport>());
        MSTestAssertions.That(() => mode.Should().Be(CqrsTransportMode.External));
        MSTestAssertions.That(() => hostedServices.Should().NotBeEmpty("the transport must start and stop with the host"));
    }

    [TestMethod]
    public void InvalidPort_IsRejectedWhenTheTransportIsConstructed() {
        // Arrange
        var options = new RabbitMqTransportOptions { Port = 0 };

        // Act
        var thrown = Capture(() => _ = new RabbitMqTransport(
            Options.Create(options),
            CreateSerializer(),
            [],
            new TransportNamingConvention()));

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<ArgumentOutOfRangeException>());
    }

    private static RabbitMqTransportOptions CreateOptions() {
        var endpoint = _endpoint ?? throw new InvalidOperationException("The RabbitMQ container is not available.");
        var virtualHost = Uri.UnescapeDataString(endpoint.AbsolutePath).Trim('/');
        var credentials = endpoint.UserInfo.Split(':', 2);
        var options = new RabbitMqTransportOptions {
            HostName = endpoint.Host,
            Port = endpoint.Port,
            UserName = Uri.UnescapeDataString(credentials[0]),
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : string.Empty,
            VirtualHost = string.IsNullOrEmpty(virtualHost) ? "/" : virtualHost,
            ClientProvidedName = "jeninnet-cqrs-rabbitmq-tests",
            ReconnectDelay = TimeSpan.FromMilliseconds(200),
            MaxReconnectDelay = TimeSpan.FromSeconds(2),
            PrefetchCount = 16,
        };
        options.Subscriptions.Add(new RabbitMqSubscription { MessageType = typeof(TransportContractMessage) });

        return options;
    }

    private static ConnectionFactory CreateConnectionFactory() {
        var endpoint = _endpoint ?? throw new InvalidOperationException("The RabbitMQ container is not available.");
        var virtualHost = Uri.UnescapeDataString(endpoint.AbsolutePath).Trim('/');
        var credentials = endpoint.UserInfo.Split(':', 2);

        return new ConnectionFactory {
            HostName = endpoint.Host,
            Port = endpoint.Port,
            UserName = Uri.UnescapeDataString(credentials[0]),
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : string.Empty,
            VirtualHost = string.IsNullOrEmpty(virtualHost) ? "/" : virtualHost,
        };
    }

    private static string UniquePrefix() =>
        $"cqrs42-{Interlocked.Increment(ref _transportCounter)}-{Guid.NewGuid():N}";

    private static JsonMessageSerializer CreateSerializer() =>
        new(Options.Create(new JsonMessageSerializerOptions()));

    private static void RequireDocker() {
        if(!_dockerAvailable || _container is null) {
            Assert.Inconclusive("Docker is not available; the RabbitMQ integration tests were skipped.");
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

    private static async Task<Exception?> CaptureAsync(Func<Task> action) {
        try {
            await action().ConfigureAwait(false);

            return null;
        }
        catch(Exception exception) {
            return exception;
        }
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

    private sealed record UnsubscribedMessage(string Name);

    private sealed class FailingHandler : IMessageTransportHandler {
        public Task HandleAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("poison message");
    }
}