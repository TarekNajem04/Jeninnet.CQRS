//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports.Tests;

/// <summary>
/// CQRS-56 consume pipeline: global and endpoint filters run in registration order before the
/// handler, a rejecting filter drops the delivery, and retry/timeout policy applies inside the
/// in-memory transport exactly as it will for broker adapters.
/// </summary>
[TestClass]
public sealed class ReceiveFilterPipelineTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task Filters_RunInRegistrationOrderBeforeTheHandlerAndSeeHeadersAsync() {
        // Arrange
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddReceiveFilter<FilteredMessage, FirstFilter>();
        services.AddReceiveFilter<FilteredMessage, SecondFilter>();
        services.ConfigureEndpoint<FilteredMessage>(endpoint => endpoint
            .Named("orders")
            .WithFilter<EndpointFilter>());
        services.AddSingleton<IMessageTransportHandler, RecordingHandler>();
        services.AddCqrsInMemoryTransport();

        using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IMessageTransport>();
        var options = new TransportMessageOptions {
            Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["trace"] = "abc-123" },
        };

        // Act
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            await transport.SendAsync(new FilteredMessage("hi"), options, TestContext.CancellationToken).ConfigureAwait(false);
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        // Assert
        MSTestAssertions.That(() => log.Should().Equal(["first", "second", "endpoint", "handler"]));
        MSTestAssertions.That(() => FirstFilter.LastHeader.Should().Be("abc-123"));
        MSTestAssertions.That(() => FirstFilter.LastEndpoint.Should().Be("orders"));
        MSTestAssertions.That(() => FirstFilter.LastMessage.Should().Be("hi"));
    }

    [TestMethod]
    public async Task RejectingFilter_DropsTheDeliveryWithoutInvokingTheHandlerAsync() {
        // Arrange
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddReceiveFilter<FilteredMessage, RejectFilter>();
        services.AddSingleton<IMessageTransportHandler, RecordingHandler>();
        services.AddCqrsInMemoryTransport();

        using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IMessageTransport>();

        // Act
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            await transport.SendAsync(new FilteredMessage("drop"), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        // Assert
        MSTestAssertions.That(() => log.Should().Equal(["reject"]));
    }

    [TestMethod]
    public async Task EndpointFilter_RunsOnlyForItsEndpointAsync() {
        // Arrange
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.ConfigureEndpoint<FilteredMessage>(endpoint => endpoint
            .Named("orders")
            .WithFilter<EndpointFilter>());
        services.AddSingleton<IMessageTransportHandler, RecordingHandler>();
        services.AddCqrsInMemoryTransport();

        using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IMessageTransport>();

        // Act
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            await transport.SendAsync(new FilteredMessage("orders"), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
            await transport.SendAsync(new OtherEndpointMessage("other"), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        // Assert
        MSTestAssertions.That(() => log.Should().Equal(["endpoint", "handler", "handler"]));
    }

    [TestMethod]
    public async Task RetryPolicy_RetriesTheHandlerInsideTheTransportAsync() {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<FlakyHandler>();
        services.AddSingleton<IMessageTransportHandler>(static provider => provider.GetRequiredService<FlakyHandler>());
        services.ConfigureEndpoint<FilteredMessage>(endpoint => endpoint
            .Named("orders")
            .WithRetry(3, TimeSpan.FromMilliseconds(1), 1.0));
        services.AddCqrsInMemoryTransport();

        using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IMessageTransport>();
        var handler = provider.GetRequiredService<FlakyHandler>();

        // Act
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            await transport.SendAsync(new FilteredMessage("retry"), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        // Assert
        MSTestAssertions.That(() => handler.Attempts.Should().Be(3));
    }

    [TestMethod]
    public async Task ConsumptionTimeout_CancelsTheHandlerAndSurfacesTimeoutAsync() {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<SlowHandler>();
        services.AddSingleton<IMessageTransportHandler>(static provider => provider.GetRequiredService<SlowHandler>());
        services.ConfigureEndpoint<FilteredMessage>(endpoint => endpoint
            .Named("orders")
            .WithConsumptionTimeout(TimeSpan.FromMilliseconds(50)));
        services.AddCqrsInMemoryTransport();

        using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IMessageTransport>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        try {
            var thrown = await CaptureAsync(() =>
                transport.SendAsync(new FilteredMessage("slow"), cancellationToken: TestContext!.CancellationToken)).ConfigureAwait(false);

            stopwatch.Stop();

            // Assert
            MSTestAssertions.That(() => thrown.Should().BeOfType<TimeoutException>());
            MSTestAssertions.That(() => ((TimeoutException)thrown!).Message.Should().Contain("orders"));
            MSTestAssertions.That(() => stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5)));
        }
        finally {
            await transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
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

    public sealed class FirstFilter(List<string> log) : IReceiveFilter<FilteredMessage> {
        public static string? LastHeader { get; private set; }

        public static string? LastEndpoint { get; private set; }

        public static string? LastMessage { get; private set; }

        public ValueTask<bool> FilterAsync(ReceiveContext context, CancellationToken cancellationToken = default) {
            log.Add("first");
            LastHeader = context.Headers["trace"];
            LastEndpoint = context.Endpoint.Name;
            LastMessage = context.MessageAs<FilteredMessage>().Name;

            return ValueTask.FromResult(true);
        }
    }

    public sealed class SecondFilter(List<string> log) : IReceiveFilter<FilteredMessage> {
        public ValueTask<bool> FilterAsync(ReceiveContext context, CancellationToken cancellationToken = default) {
            log.Add("second");

            return ValueTask.FromResult(true);
        }
    }

    public sealed class EndpointFilter(List<string> log) : IReceiveFilter<FilteredMessage> {
        public ValueTask<bool> FilterAsync(ReceiveContext context, CancellationToken cancellationToken = default) {
            log.Add("endpoint");

            return ValueTask.FromResult(true);
        }
    }

    public sealed class RejectFilter(List<string> log) : IReceiveFilter<FilteredMessage> {
        public ValueTask<bool> FilterAsync(ReceiveContext context, CancellationToken cancellationToken = default) {
            log.Add("reject");

            return ValueTask.FromResult(false);
        }
    }

    public sealed class RecordingHandler(List<string> log) : IMessageTransportHandler {
        public Task HandleAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default) {
            log.Add("handler");

            return Task.CompletedTask;
        }
    }

    public sealed class FlakyHandler : IMessageTransportHandler {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        public Task HandleAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default) {
            var attempt = Interlocked.Increment(ref _attempts);

            return attempt < 3
                ? Task.FromException(new InvalidOperationException("transient"))
                : Task.CompletedTask;
        }
    }

    public sealed class SlowHandler : IMessageTransportHandler {
        public async Task HandleAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default) =>
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>A filtered message used by the receive pipeline tests.</summary>
/// <param name="Name">A payload.</param>
public sealed record FilteredMessage(string Name);