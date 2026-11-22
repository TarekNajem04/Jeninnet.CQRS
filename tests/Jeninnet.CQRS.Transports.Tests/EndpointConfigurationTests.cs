//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//

namespace Jeninnet.CQRS.Transports.Tests;

/// <summary>
/// CQRS-56 endpoint topology: immutable configuration, startup validation, per-endpoint filters,
/// and the adapter-facing registry that carries prefetch/concurrency to every transport.
/// </summary>
[TestClass]
public sealed class EndpointConfigurationTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task ConfigureEndpoint_CarriesEveryPolicyValueToTheAdapterAsync() {
        // Arrange
        var services = new ServiceCollection();
        services.ConfigureEndpoint<EndpointMessage>(endpoint => endpoint
            .Named("orders")
            .WithPrefetch(32)
            .WithConcurrency(4)
            .WithRetry(3, TimeSpan.FromMilliseconds(25), 2.0)
            .WithDeadLetterTarget("orders.dlq")
            .WithConsumptionTimeout(TimeSpan.FromSeconds(5))
            .WithFilter<OrdersFilter>());

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<EndpointConfigurationRegistry>();
        var adapter = new RecordingAdapter(registry);

        // Act
        await adapter.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        var configuration = registry.Find(typeof(EndpointMessage));

        // Assert
        MSTestAssertions.That(() => configuration.Should().NotBeNull());
        var resolved = registry.Find(typeof(EndpointMessage))!;
        MSTestAssertions.That(() => resolved.Name.Should().Be("orders"));
        MSTestAssertions.That(() => resolved.Retry.MaxAttempts.Should().Be(3));
        MSTestAssertions.That(() => resolved.DeadLetterTarget.Should().Be("orders.dlq"));
        MSTestAssertions.That(() => resolved.Filters.Should().Contain(typeof(OrdersFilter)));
        MSTestAssertions.That(() => adapter.Prefetch.Should().Be(32));
        MSTestAssertions.That(() => adapter.Concurrency.Should().Be(4));
        MSTestAssertions.That(() => registry.GetRequired("orders").Should().BeSameAs(resolved));
    }

    [TestMethod]
    public async Task InvalidEndpoint_FailsStartupWithTheEndpointNameAndValueAsync() {
        // Arrange
        var services = new ServiceCollection();
        services.ConfigureEndpoint<EndpointMessage>(endpoint => endpoint.Named("orders").WithPrefetch(-1));

        using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().OfType<EndpointConfigurationHostedService>().Single();

        // Act
        var thrown = await CaptureAsync(() => hosted.StartAsync(TestContext!.CancellationToken)).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<EndpointConfigurationException>());
        var failure = (EndpointConfigurationException)thrown!;
        MSTestAssertions.That(() => failure.EndpointName.Should().Be("orders"));
        MSTestAssertions.That(() => failure.Message.Should().Contain("orders"));
        MSTestAssertions.That(() => failure.Message.Should().Contain("prefetch"));
    }

    [TestMethod]
    public async Task FilterForUnknownEndpoint_FailsStartupWithTheMissingNameAsync() {
        // Arrange
        var services = new ServiceCollection();
        services.ConfigureEndpoint<EndpointMessage>(endpoint => endpoint.Named("orders"));
        services.AddReceiveFilter<EndpointMessage, PassThroughFilter>("missing");

        using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().OfType<EndpointConfigurationHostedService>().Single();

        // Act
        var thrown = await CaptureAsync(() => hosted.StartAsync(TestContext!.CancellationToken)).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<EndpointConfigurationException>());
        var failure = (EndpointConfigurationException)thrown!;
        MSTestAssertions.That(() => failure.EndpointName.Should().Be("missing"));
        MSTestAssertions.That(() => failure.Message.Should().Contain("unknown endpoint"));
        MSTestAssertions.That(() => failure.Message.Should().Contain("missing"));
    }

    [TestMethod]
    public async Task DuplicateEndpointNames_FailStartupAsync() {
        // Arrange
        var services = new ServiceCollection();
        services.ConfigureEndpoint<EndpointMessage>(endpoint => endpoint.Named("shared"));
        services.ConfigureEndpoint<OtherEndpointMessage>(endpoint => endpoint.Named("shared"));

        using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().OfType<EndpointConfigurationHostedService>().Single();

        // Act
        var thrown = await CaptureAsync(() => hosted.StartAsync(TestContext!.CancellationToken)).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<EndpointConfigurationException>());
        MSTestAssertions.That(() => ((EndpointConfigurationException)thrown!).EndpointName.Should().Be("shared"));
    }

    [TestMethod]
    public void UnknownEndpointName_ThrowsWithTheName() {
        // Arrange
        var services = new ServiceCollection();
        services.AddCqrsReceivePipeline();
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<EndpointConfigurationRegistry>();

        // Act
        Exception? thrown = null;

        try {
            _ = registry.GetRequired("missing");
        }
        catch(Exception exception) {
            thrown = exception;
        }

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<EndpointConfigurationException>());
        MSTestAssertions.That(() => ((EndpointConfigurationException)thrown!).EndpointName.Should().Be("missing"));
    }

    [TestMethod]
    public void EndpointConfiguration_DefaultForUsesTheTypeName() {
        // Act
        var configuration = EndpointConfiguration.DefaultFor(typeof(EndpointMessage));

        // Assert
        MSTestAssertions.That(() => configuration.Name.Should().Be(nameof(EndpointMessage)));
        MSTestAssertions.That(() => configuration.PrefetchCount.Should().Be(16));
        MSTestAssertions.That(() => configuration.ConcurrentLimit.Should().Be(1));
        MSTestAssertions.That(() => configuration.ConsumptionTimeout.Should().Be(Timeout.InfiniteTimeSpan));
        MSTestAssertions.That(() => configuration.Retry.Should().Be(EndpointRetryPolicy.None));
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

    public sealed class PassThroughFilter : IReceiveFilter<EndpointMessage> {
        public ValueTask<bool> FilterAsync(ReceiveContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    public sealed class OrdersFilter : IReceiveFilter<EndpointMessage> {
        public ValueTask<bool> FilterAsync(ReceiveContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class RecordingAdapter(EndpointConfigurationRegistry registry) : IMessageTransport {
        private readonly EndpointConfigurationRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

        public int Prefetch { get; private set; }

        public int Concurrency { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default) {
            var configuration = _registry.Find(typeof(EndpointMessage));
            Prefetch = configuration?.PrefetchCount ?? 0;
            Concurrency = configuration?.ConcurrentLimit ?? 0;

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task PublishAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}

/// <summary>A message used by the endpoint configuration tests.</summary>
/// <param name="Name">A payload.</param>
public sealed record EndpointMessage(string Name);

/// <summary>A second message used to prove endpoint names are unique.</summary>
/// <param name="Name">A payload.</param>
public sealed record OtherEndpointMessage(string Name);
