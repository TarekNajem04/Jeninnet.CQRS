//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// Characterization tests for CQRS-16: one-generic void handlers must dispatch, with the
/// exact two-generic registration winning on conflict.
/// </summary>
[TestClass]
public class VoidCommandDispatchTests {
    private static ServiceCollection BaseServices(List<string> calls) {
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();

        return services;
    }

    [TestMethod]
    public async Task ManualOneParamRegistration_DispatchesAsync() {
        // Arrange
        var calls = new List<string>();
        var services = BaseServices(calls);
        services.AddTransient<ICommandHandler<VoidCommand>, OneParamHandler>();
        var broker = services.BuildServiceProvider().GetRequiredService<ICqrsBroker>();

        // Act
        await broker.DispatchAsync(new VoidCommand("Hi"), default);

        // Assert: the fallback resolved the one-generic handler.
        MSTestAssertions.That(() => calls.Should().Equal(["one:Hi"]));
    }

    [TestMethod]
    public async Task ExactTwoParamRegistration_WinsOverOneParamAsync() {
        // Arrange
        var calls = new List<string>();
        var services = BaseServices(calls);
        services.AddTransient<ICommandHandler<VoidCommand>, OneParamHandler>();
        services.AddTransient<ICommandHandler<VoidCommand, Unit>, TwoParamHandler>();
        var broker = services.BuildServiceProvider().GetRequiredService<ICqrsBroker>();

        // Act
        await broker.DispatchAsync(new VoidCommand("Hi"), default);

        // Assert: the exact registration wins; the fallback never fires.
        MSTestAssertions.That(() => calls.Should().Equal(["two:Hi"]));
    }

    [TestMethod]
    public async Task ScannedOneParamRegistration_DispatchesAsync() {
        // Arrange: reflection scan registers the one-generic form as declared.
        // Uses its own message type so the sibling VoidCommand handlers in this
        // assembly cannot interfere (scan registers every handler it finds).
        var calls = new List<string>();
        var services = BaseServices(calls);
        services.AddCqrsByReflection(
            new Configurations.CqrsOptions(new Configurations.PipelineBehaviorsOptions(EnableMetrics: false, EnableErrorHandling: false, EnableValidation: false, EnableTransaction: false, EnableLogging: false)),
            typeof(VoidCommandDispatchTests).Assembly);
        var broker = services.BuildServiceProvider().GetRequiredService<ICqrsBroker>();

        // Act
        await broker.DispatchAsync(new VoidScannedCommand("Hi"), default);

        // Assert: the fallback resolves the scanned one-generic handler.
        MSTestAssertions.That(() => calls.Should().Contain("one-scanned:Hi"));
    }

    private sealed record VoidCommand(string Name) : ICommand;

    private sealed record VoidScannedCommand(string Name) : ICommand;

    private sealed class OneParamHandler(List<string> calls) : ICommandHandler<VoidCommand> {
        public Task<Unit> HandleAsync(VoidCommand command, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add($"one:{command.Name}");
            return Task.FromResult(Unit.Value);
        }
    }

    private sealed class TwoParamHandler(List<string> calls) : ICommandHandler<VoidCommand, Unit> {
        public Task<Unit> HandleAsync(VoidCommand command, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add($"two:{command.Name}");
            return Task.FromResult(Unit.Value);
        }
    }

    private sealed class ScannedOneParamHandler(List<string> calls) : ICommandHandler<VoidScannedCommand> {
        public Task<Unit> HandleAsync(VoidScannedCommand command, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add($"one-scanned:{command.Name}");
            return Task.FromResult(Unit.Value);
        }
    }
}
