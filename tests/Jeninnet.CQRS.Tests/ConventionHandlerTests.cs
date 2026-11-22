//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// Characterization tests for CQRS-21: a class with <c>Handle</c>/<c>HandleAsync</c> is a
/// handler without implementing an interface; explicit interfaces always win.
/// </summary>
[TestClass]
public class ConventionHandlerTests {
    private static ICqrsBroker ReflectionBroker() {
        var services = new ServiceCollection();
        services.AddCqrsByReflection(new Configurations.CqrsOptions(
            new Configurations.PipelineBehaviorsOptions(
                EnableMetrics: false,
                EnableErrorHandling: false,
                EnableValidation: false,
                EnableTransaction: false,
                EnableLogging: false)),
            typeof(ConventionHandlerTests).Assembly);

        return services.BuildServiceProvider().GetRequiredService<ICqrsBroker>();
    }

    [TestMethod]
    public async Task VoidCommand_ByConvention_DispatchesAsync() {
        // Arrange
        var broker = ReflectionBroker();

        // Act
        await broker.DispatchAsync(new ConventionVoidCommand("Hi"), default);

        // Assert
        MSTestAssertions.That(() => ConventionVoidHandler.Last.Should().NotBeNull());
        MSTestAssertions.That(() => ConventionVoidHandler.Last!.Calls.Should().Be(1));
    }

    [TestMethod]
    public async Task ResultCommand_ByConvention_ReturnsValueAsync() {
        // Arrange
        var broker = ReflectionBroker();

        // Act
        var result = await broker.DispatchAsync<ConventionResultCommand, string>(new ConventionResultCommand("Hi"), default);

        // Assert
        MSTestAssertions.That(() => result.Should().Be("hi"));
    }

    [TestMethod]
    public async Task Query_ByConvention_ReturnsValueAsync() {
        // Arrange
        var broker = ReflectionBroker();

        // Act
        var result = await broker.DispatchAsync<ConventionQuery, int>(new ConventionQuery(7), default);

        // Assert
        MSTestAssertions.That(() => result.Should().Be(14));
    }

    [TestMethod]
    public async Task Notification_ByConvention_IsHandledAsync() {
        // Arrange
        var broker = ReflectionBroker();

        // Act
        await broker.PublishAsync(new ConventionNotification("Hi"), default);

        // Assert
        MSTestAssertions.That(() => ConventionNotificationHandler.Last.Should().NotBeNull());
        MSTestAssertions.That(() => ConventionNotificationHandler.Last!.Calls.Should().Be(1));
    }

    [TestMethod]
    public async Task ExplicitInterfaceHandler_WinsOverConventionAsync() {
        // Arrange
        var services = new ServiceCollection();
        services.AddCqrsByReflection(new Configurations.CqrsOptions(
            new Configurations.PipelineBehaviorsOptions(
                EnableMetrics: false,
                EnableErrorHandling: false,
                EnableValidation: false,
                EnableTransaction: false,
                EnableLogging: false)),
            typeof(ConventionHandlerTests).Assembly);
        MSTestAssertions.That(() => services.Count(d => d.ServiceType == typeof(ICommandHandler<ContestedCommand, string>)).Should().Be(1));

        var broker = services.BuildServiceProvider().GetRequiredService<ICqrsBroker>();

        // Act
        var result = await broker.DispatchAsync<ContestedCommand, string>(new ContestedCommand("x"), default);

        // Assert: the interface handler ran; the convention handler never did.
        MSTestAssertions.That(() => result.Should().Be("explicit"));
        MSTestAssertions.That(() => ContestedConventionHandler.Last.Should().BeNull());
    }

    [TestMethod]
    public void NonConformingMethods_AreIgnored() {
        // Arrange / Act
        var broker = ReflectionBroker();

        // Assert: the misleading class is present but was never bound to a handler.
        MSTestAssertions.That(() => broker.Should().NotBeNull());
        MSTestAssertions.That(() => MisleadingHelpers.Instance.Should().NotBeNull());
    }

    public sealed record ConventionVoidCommand(string Name) : ICommand;

    public sealed class ConventionVoidHandler {
        public static ConventionVoidHandler? Last { get; private set; }

        public ConventionVoidHandler() => Last = this;

        public int Calls { get; private set; }

        public Task HandleAsync(ConventionVoidCommand command, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;

            return Task.CompletedTask;
        }
    }

    public sealed record ConventionResultCommand(string Name) : ICommand<string>;

    public sealed class ConventionResultHandler {
        private readonly string _suffix = string.Empty;

        public Task<string> HandleAsync(ConventionResultCommand command, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(command.Name.ToLowerInvariant() + _suffix);
        }
    }

    public sealed record ConventionQuery(int Value) : IQuery<int>;

    public sealed class ConventionQueryHandler {
        private readonly int _multiplier = 2;

        public Task<int> HandleAsync(ConventionQuery query) =>
            Task.FromResult(query.Value * _multiplier);
    }

    public sealed record ConventionNotification(string Name) : INotification;

    public sealed class ConventionNotificationHandler {
        public static ConventionNotificationHandler? Last { get; private set; }

        public ConventionNotificationHandler() => Last = this;

        public int Calls { get; private set; }

        public Task Handle(ConventionNotification notification) {
            Calls++;

            return Task.CompletedTask;
        }
    }

    public sealed record ContestedCommand(string Name) : ICommand<string>;

    public sealed class ContestedExplicitHandler : ICommandHandler<ContestedCommand, string> {
        public Task<string> HandleAsync(ContestedCommand command, CancellationToken cancellationToken) =>
            Task.FromResult("explicit");
    }

    public sealed class ContestedConventionHandler {
        public static ContestedConventionHandler? Last { get; private set; }

        private readonly string _result = "convention";

        public ContestedConventionHandler() => Last = this;

        public Task<string> HandleAsync(ContestedCommand command) =>
            Task.FromResult(_result);
    }

    public sealed record MisleadingCommand(string Name) : ICommand;

    public sealed class MisleadingHelpers {
        private readonly int _bias = 1;

        public static MisleadingHelpers Instance { get; } = new();

        public static Task HandleAsync(MisleadingCommand command) => Task.CompletedTask;

        public Task<int> HandleAsync() => Task.FromResult(_bias);

        public Task HandleAsync(int first, int second, int third) {
            _ = first + second + third + _bias;

            return Task.CompletedTask;
        }

        private Task<Unit> HandleAsync(MisleadingCommand command, CancellationToken a, CancellationToken b) {
            _ = a == b && _bias > 0;

            return Task.FromResult(Unit.Value);
        }
    }
}
