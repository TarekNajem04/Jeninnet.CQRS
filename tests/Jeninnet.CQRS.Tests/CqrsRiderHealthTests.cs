//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// CQRS-63 rider health contribution: one entry per rider in the result data, degraded while the
/// host has not started, healthy after every rider starts.
/// </summary>
[TestClass]
public sealed class CqrsRiderHealthTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task RiderHealth_ReportsEachRiderByNameAsync() {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCqrsRider("alpha", new PassiveTransport());
        services.AddCqrsRider("beta", new PassiveTransport());
        services.AddCqrsRiderHealthChecks(tags: ["broker"]);

        using var provider = services.BuildServiceProvider();
        var health = provider.GetRequiredService<HealthCheckService>();
        var host = provider.GetRequiredService<CqrsRiderHost>();

        // Act: before start every rider is registered but not running.
        var before = await health.CheckHealthAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        await host.StartAsync(TestContext.CancellationToken).ConfigureAwait(false);

        try {
            var after = await health.CheckHealthAsync(TestContext.CancellationToken).ConfigureAwait(false);

            // Assert
            MSTestAssertions.That(() => before.Entries.Should().ContainKey("riders"));
            MSTestAssertions.That(() => before.Entries["riders"].Status.Should().Be(HealthStatus.Degraded));
            MSTestAssertions.That(() => before.Entries["riders"].Data.Should().ContainKey("rider.alpha"));
            MSTestAssertions.That(() => before.Entries["riders"].Data.Should().ContainKey("rider.beta"));
            MSTestAssertions.That(() => after.Entries["riders"].Status.Should().Be(HealthStatus.Healthy));
            MSTestAssertions.That(() => after.Entries["riders"].Data["rider.alpha"].Should().Be("Started"));
            MSTestAssertions.That(() => after.Entries["riders"].Data["rider.beta"].Should().Be("Started"));
            MSTestAssertions.That(() => after.Entries["riders"].Tags.Should().Contain("broker"));
        }
        finally {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private sealed class PassiveTransport : IMessageTransport {
        public Task SendAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task PublishAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}