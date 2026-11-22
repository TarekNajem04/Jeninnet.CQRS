//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// CQRS-60 message observers: send/publish/consume pre/post/fault ordering, observer exception
/// isolation, and the zero-allocation no-observer fast path.
/// </summary>
[TestClass]
public sealed class MessageObserverTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task SendPublishAndConsume_FireInRegistrationOrderAsync() {
        // Arrange
        var log = new List<string>();
        await using var provider = BuildProvider(log);
        var transport = provider.GetRequiredService<IMessageTransport>();
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            // Act
            await transport.SendAsync(new ObserverMessage("send"), cancellationToken: TestContext!.CancellationToken).ConfigureAwait(false);
            await transport.PublishAsync(new ObserverMessage("publish"), cancellationToken: TestContext!.CancellationToken).ConfigureAwait(false);
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        // Assert
        MSTestAssertions.That(() => log.Should().Equal([
            "send:before",
            "consume:before",
            "handler",
            "consume:after",
            "send:after",
            "publish:before",
            "consume:before",
            "handler",
            "consume:after",
            "publish:after",
        ]));
    }

    [TestMethod]
    public async Task FaultingHandler_FiresConsumeAndSendFaultObserversAsync() {
        // Arrange
        var log = new List<string>();
        await using var provider = BuildProvider(log, throwingHandler: true);
        var transport = provider.GetRequiredService<IMessageTransport>();
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            // Act
            var thrown = await CaptureAsync(() => transport.SendAsync(
                new ObserverMessage("fault"),
                cancellationToken: TestContext!.CancellationToken)).ConfigureAwait(false);

            // Assert
            MSTestAssertions.That(() => thrown.Should().BeOfType<InvalidOperationException>());
            MSTestAssertions.That(() => log.Should().Contain("consume:fault"));
            MSTestAssertions.That(() => log.Should().Contain("send:fault"));
            MSTestAssertions.That(() => log.Should().NotContain("send:after"));
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task ThrowingObserver_IsIsolatedAndTheSendStillRunsAsync() {
        // Arrange
        var log = new List<string>();
        long faults = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = static (instrument, current) => {
            if(instrument.Meter.Name == MessageObserverDiagnosticNames.MeterName &&
               instrument.Name == MessageObserverDiagnosticNames.Faults) {
                current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => Interlocked.Add(ref faults, value));
        listener.Start();

        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddSendObserver<ThrowingSendObserver>();
        services.AddSendObserver<RecordingObserver>();
        services.AddConsumeObserver<RecordingObserver>();
        services.AddSingleton<IMessageTransportHandler, RecordingHandler>();
        services.AddCqrsInMemoryTransport();
        await using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IMessageTransport>();
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            // Act
            await transport.SendAsync(new ObserverMessage("ok"), cancellationToken: TestContext!.CancellationToken).ConfigureAwait(false);

            // Assert: the throwing observer was isolated and counted; the next observer and the handler ran.
            MSTestAssertions.That(() => log.Should().Equal(["send:before", "consume:before", "handler", "consume:after", "send:after"]));
            MSTestAssertions.That(() => Volatile.Read(ref faults).Should().Be(1));
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public void NoObservers_DoesNotAllocateOnTheInvokerFastPath() {
        // Arrange
        var invoker = new MessageObserverInvoker();
        var context = new OutboundObservationContext(
            new object(),
            typeof(object),
            new TransportEnvelope(
                "m-1",
                typeof(object).AssemblyQualifiedName ?? nameof(Object),
                "application/json",
                ReadOnlyMemory<byte>.Empty,
                null,
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new TransportTopology("exchange", "queue", "topic", "key"),
                TransportDeliveryMode.Send));

        _ = invoker.OnSendingAsync(context);

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();

        for(var index = 0; index < 1_000; index++) {
            _ = invoker.OnSendingAsync(context);
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        // Assert
        MSTestAssertions.That(() => invoker.HasSendObservers.Should().BeFalse());
        MSTestAssertions.That(() => (after - before).Should().Be(0));
    }

    private static ServiceProvider BuildProvider(List<string> log, bool throwingHandler = false) {
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddSingleton<RecordingObserver>();
        services.AddSendObserver<RecordingObserver>();
        services.AddPublishObserver<RecordingObserver>();
        services.AddConsumeObserver<RecordingObserver>();

        if(throwingHandler) {
            services.AddSingleton<IMessageTransportHandler, ThrowingHandler>();
        } else {
            services.AddSingleton<IMessageTransportHandler, RecordingHandler>();
        }

        services.AddCqrsInMemoryTransport();

        return services.BuildServiceProvider();
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

    public sealed record ObserverMessage(string Name);

    public sealed class RecordingObserver(List<string> log) : ISendObserver, IPublishObserver, IConsumeObserver {
        private readonly List<string> _log = log ?? throw new ArgumentNullException(nameof(log));

        public Task OnSendingAsync(OutboundObservationContext context, CancellationToken cancellationToken = default) {
            _log.Add("send:before");

            return Task.CompletedTask;
        }

        public Task OnSentAsync(OutboundObservationContext context, CancellationToken cancellationToken = default) {
            _log.Add("send:after");

            return Task.CompletedTask;
        }

        public Task OnSendFaultAsync(OutboundObservationContext context, Exception error, CancellationToken cancellationToken = default) {
            _log.Add("send:fault");

            return Task.CompletedTask;
        }

        public Task OnPublishingAsync(OutboundObservationContext context, CancellationToken cancellationToken = default) {
            _log.Add("publish:before");

            return Task.CompletedTask;
        }

        public Task OnPublishedAsync(OutboundObservationContext context, CancellationToken cancellationToken = default) {
            _log.Add("publish:after");

            return Task.CompletedTask;
        }

        public Task OnPublishFaultAsync(OutboundObservationContext context, Exception error, CancellationToken cancellationToken = default) {
            _log.Add("publish:fault");

            return Task.CompletedTask;
        }

        public Task OnConsumingAsync(ConsumeObservationContext context, CancellationToken cancellationToken = default) {
            _log.Add("consume:before");

            return Task.CompletedTask;
        }

        public Task OnConsumedAsync(ConsumeObservationContext context, CancellationToken cancellationToken = default) {
            _log.Add("consume:after");

            return Task.CompletedTask;
        }

        public Task OnConsumeFaultAsync(ConsumeObservationContext context, Exception error, CancellationToken cancellationToken = default) {
            _log.Add("consume:fault");

            return Task.CompletedTask;
        }
    }

    public sealed class ThrowingSendObserver : ISendObserver {
        public Task OnSendingAsync(OutboundObservationContext context, CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException("observer boom"));

        public Task OnSentAsync(OutboundObservationContext context, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task OnSendFaultAsync(OutboundObservationContext context, Exception error, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    public sealed class RecordingHandler(List<string> log) : IMessageTransportHandler {
        private readonly List<string> _log = log ?? throw new ArgumentNullException(nameof(log));

        public Task HandleAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default) {
            _log.Add("handler");

            return Task.CompletedTask;
        }
    }

    public sealed class ThrowingHandler : IMessageTransportHandler {
        public Task HandleAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException("handler boom"));
    }
}
