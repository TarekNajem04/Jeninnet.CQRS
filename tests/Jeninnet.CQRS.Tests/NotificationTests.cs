//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

[TestClass]
public class NotificationTests {
    [TestMethod]
    public async Task PublishAsync_InvokesAllHandlers_InOrderAsync() {
        // Arrange
        var (provider, log) = BuildProvider(NotificationPublishStrategy.ForEachAwait);
        var publisher = provider.GetRequiredService<INotificationPublisher>();

        // Act
        await publisher.PublishAsync(new TestEvent("Hi"), default);

        // Assert
        MSTestAssertions.That(() => log.Calls.Should().Equal(["first:Hi", "second:Hi"]));
    }

    [TestMethod]
    public async Task PublishAsync_WithNoHandlers_IsNoOpAsync() {
        // Arrange
        var services = new ServiceCollection();
        services.AddCqrsNotifications();
        var publisher = services.BuildServiceProvider().GetRequiredService<INotificationPublisher>();

        // Act: must not throw.
        await publisher.PublishAsync(new TestEvent("Hi"), default);
    }

    [TestMethod]
    public async Task ForEachAwait_StopsAtFirstFailureAsync() {
        // Arrange
        var (provider, log) = BuildProvider(NotificationPublishStrategy.ForEachAwait, failingFirst: true);
        var publisher = provider.GetRequiredService<INotificationPublisher>();

        // Act
        InvalidOperationException? thrown = null;
        try {
            await publisher.PublishAsync(new TestEvent("Hi"), default);
        }
        catch(InvalidOperationException ex) {
            thrown = ex;
        }

        // Assert: fail-fast — the second handler never ran, the original error surfaced.
        MSTestAssertions.That(() => thrown.Should().NotBeNull());
        MSTestAssertions.That(() => thrown!.Message.Should().Be("boom"));
        MSTestAssertions.That(() => log.Calls.Should().Equal(["first:Hi"]));
    }

    [TestMethod]
    public async Task Parallel_StartsEveryHandlerAsync() {
        // Arrange
        var (provider, log) = BuildProvider(NotificationPublishStrategy.Parallel, failingFirst: true);
        var publisher = provider.GetRequiredService<INotificationPublisher>();

        // Act
        InvalidOperationException? thrown = null;
        try {
            await publisher.PublishAsync(new TestEvent("Hi"), default);
        }
        catch(InvalidOperationException ex) {
            thrown = ex;
        }

        // Assert: the slow failing handler and the fast handler both started.
        MSTestAssertions.That(() => thrown.Should().NotBeNull());
        MSTestAssertions.That(() => thrown!.Message.Should().Be("boom"));
        MSTestAssertions.That(() => log.Calls.Should().Contain("second:Hi"));
    }

    [TestMethod]
    public async Task PublishAsync_ObservesCancellationAsync() {
        // Arrange: the failing handler awaits the token, so a cancelled token throws before any work.
        var (provider, _) = BuildProvider(NotificationPublishStrategy.ForEachAwait, failingFirst: true);
        var publisher = provider.GetRequiredService<INotificationPublisher>();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        OperationCanceledException? thrown = null;
        try {
            await publisher.PublishAsync(new TestEvent("Hi"), cts.Token);
        }
        catch(OperationCanceledException ex) {
            thrown = ex;
        }

        // Assert
        MSTestAssertions.That(() => thrown.Should().NotBeNull());
    }

    [TestMethod]
    public async Task Broker_PublishAsync_DelegatesToPublisherAsync() {
        // Arrange: dogfooded onto the test kit — the broker records, the kit asserts.
        var services = new ServiceCollection();
        services.AddSingleton<CallLog>();
        services.AddTransient<INotificationHandler<TestEvent>, FirstHandler>();
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddCqrsNotifications();
        var broker = new TestCqrsBroker(services.BuildServiceProvider().GetRequiredService<ICqrsBroker>());

        // Act
        await broker.PublishAsync(new TestEvent("Hi"), default);

        // Assert
        broker.ShouldHavePublished<TestEvent>(e => e.Message == "Hi");
    }

    [TestMethod]
    public async Task Broker_PublishAsync_WithoutPublisher_ExplainsItselfAsync() {
        // Arrange: dispatchers only, no publisher registered.
        var services = new ServiceCollection();
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        var broker = services.BuildServiceProvider().GetRequiredService<ICqrsBroker>();

        // Act
        InvalidOperationException? thrown = null;
        try {
            await broker.PublishAsync(new TestEvent("Hi"), default);
        }
        catch(InvalidOperationException ex) {
            thrown = ex;
        }

        // Assert
        MSTestAssertions.That(() => thrown.Should().NotBeNull());
        MSTestAssertions.That(() => thrown!.Message.Should().Contain("AddCqrsNotifications"));
    }

    private static (IServiceProvider Provider, CallLog Log) BuildProvider(NotificationPublishStrategy strategy, bool failingFirst = false) {
        var services = new ServiceCollection();
        var log = new CallLog();
        services.AddSingleton(log);

        if(failingFirst) {
            services.AddTransient<INotificationHandler<TestEvent>, FailingFirstHandler>();
        } else {
            services.AddTransient<INotificationHandler<TestEvent>, FirstHandler>();
        }

        services.AddTransient<INotificationHandler<TestEvent>, SecondHandler>();
        services.AddCqrsNotifications(o => o.Strategy = strategy);

        return (services.BuildServiceProvider(), log);
    }

    private sealed record TestEvent(string Message) : INotification;

    private sealed class CallLog {
        public readonly List<string> Calls = [];
    }

    private sealed class FirstHandler(CallLog log) : INotificationHandler<TestEvent> {
        public Task HandleAsync(TestEvent notification, CancellationToken cancellationToken) {
            log.Calls.Add($"first:{notification.Message}");
            return Task.CompletedTask;
        }
    }

    private sealed class SecondHandler(CallLog log) : INotificationHandler<TestEvent> {
        public Task HandleAsync(TestEvent notification, CancellationToken cancellationToken) {
            log.Calls.Add($"second:{notification.Message}");
            return Task.CompletedTask;
        }
    }

    private sealed class FailingFirstHandler(CallLog log) : INotificationHandler<TestEvent> {
        public async Task HandleAsync(TestEvent notification, CancellationToken cancellationToken) {
            log.Calls.Add($"first:{notification.Message}");
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("boom");
        }
    }
}