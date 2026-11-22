//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// Characterization tests for CQRS-23: typed exception actions observe a failure, typed
/// exception handlers turn it into a response, most-specific wins, retries run first, and
/// cancellation is never intercepted.
/// </summary>
[TestClass]
public class ExceptionPolicyTests {
    private static (ICqrsBroker Broker, List<string> Log) Build(Action<IServiceCollection>? configure = null) {
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();

        services.AddTransient<ICommandHandler<FailingCommand, string>, FailingHandler>();
        services.AddTransient<ICommandHandler<VoidFailingCommand>, VoidFailingHandler>();
        services.AddTransient<ICommandHandler<AmbiguousCommand, string>, AmbiguousFailingHandler>();
        services.AddTransient<ICommandHandler<CancelCommand, string>, CancelHandler>();
        services.AddTransient<ICommandHandler<RetryCommand, string>, AlwaysFailingHandler>();
        services.AddTransient<ICommandHandler<ActionOnlyCommand, string>, ActionOnlyHandler>();

        services.AddTransient<ICqrsExceptionHandler<FailingCommand, string>, TimeoutFallback>();
        services.AddTransient<ICqrsExceptionHandler<VoidFailingCommand, Unit>, VoidTimeoutFallback>();
        services.AddTransient<ICqrsExceptionHandler<AmbiguousCommand, string>, BaseExceptionFallback>();
        services.AddTransient<ICqrsExceptionHandler<AmbiguousCommand, string>, SpecificExceptionFallback>();
        services.AddTransient<ICqrsExceptionHandler<CancelCommand, string>, CancelledFallback>();
        services.AddTransient<ICqrsExceptionAction<RetryCommand>, RetryAction>();
        services.AddTransient<ICqrsExceptionHandler<RetryCommand, string>, RetryFallback>();
        services.AddTransient<ICqrsExceptionAction<ActionOnlyCommand>, ActionOnlyObserver>();

        configure?.Invoke(services);

        return (services.BuildServiceProvider().GetRequiredService<ICqrsBroker>(), log);
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

    [TestMethod]
    public async Task Handler_ReplacesResponseAsync() {
        // Arrange
        var (broker, log) = Build();

        // Act
        var result = await broker.DispatchAsync<FailingCommand, string>(new FailingCommand(), default);

        // Assert: the pipeline returned the handler's value instead of throwing.
        MSTestAssertions.That(() => result.Should().Be("fallback"));
        MSTestAssertions.That(() => log.Should().Equal(["attempt", "handled"], string.Join(",", log)));
    }

    [TestMethod]
    public async Task MostSpecificHandler_WinsAsync() {
        // Arrange
        var (broker, log) = Build();

        // Act
        var result = await broker.DispatchAsync<AmbiguousCommand, string>(new AmbiguousCommand(), default);

        // Assert: the InvalidOperationException handler beats the Exception handler.
        MSTestAssertions.That(() => result.Should().Be("specific"));
        MSTestAssertions.That(() => log.Should().Equal(["attempt", "specific"], string.Join(",", log)));
    }

    [TestMethod]
    public async Task Action_ObservesAndOriginalExceptionPropagatesAsync() {
        // Arrange: an action for this request, no handler.
        var (broker, log) = Build();

        // Act
        var thrown = await CaptureAsync(() => broker.DispatchAsync<ActionOnlyCommand, string>(new ActionOnlyCommand(), default));

        // Assert: observed once, same instance rethrown.
        MSTestAssertions.That(() => thrown.Should().BeOfType<TimeoutException>());
        MSTestAssertions.That(() => log.Should().Equal(["attempt", "observed"], string.Join(",", log)));
    }

    [TestMethod]
    public async Task Actions_RunBeforeHandlersAsync() {
        // Arrange
        var (broker, log) = Build();

        // Act
        var result = await broker.DispatchAsync<RetryCommand, string>(new RetryCommand(), default);

        // Assert: action first, then the handler.
        MSTestAssertions.That(() => result.Should().Be("handled-by-retry"));
        MSTestAssertions.That(() => log.Should().Equal(["attempt", "action", "handler"], string.Join(",", log)));
    }

    [TestMethod]
    public async Task VoidCommand_PolicyAppliesAsync() {
        // Arrange
        var (broker, log) = Build();

        // Act
        await broker.DispatchAsync(new VoidFailingCommand(), default);

        // Assert: the void path returns without throwing thanks to the Unit handler.
        MSTestAssertions.That(() => log.Should().Equal(["void-attempt", "void-handled"], string.Join(",", log)));
    }

    [TestMethod]
    public async Task Cancellation_IsNeverHandledAsync() {
        // Arrange
        var (broker, log) = Build();

        // Act
        var thrown = await CaptureAsync(() => broker.DispatchAsync<CancelCommand, string>(new CancelCommand(), default));

        // Assert: the CancelledFallback handler exists but must not run.
        MSTestAssertions.That(() => thrown.Should().BeOfType<OperationCanceledException>());
        MSTestAssertions.That(() => log.Should().BeEmpty(string.Join(",", log)));
    }

    [TestMethod]
    public async Task Retry_RunsFirst_ThenPolicyOnceAsync() {
        // Arrange: three attempts, all failing, then the typed handler.
        var (broker, log) = Build(services => {
            services.AddSingleton<IOptions<RetryBehaviorOptions>>(new RetryBehaviorOptions {
                RetryCount = 3,
                BaseDelayMilliseconds = 1,
            });
            services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(RetryBehavior<,>));
        });

        // Act
        var result = await broker.DispatchAsync<RetryCommand, string>(new RetryCommand(), default);

        // Assert: retries exhausted, then the policy handled once.
        MSTestAssertions.That(() => result.Should().Be("handled-by-retry"));
        MSTestAssertions.That(() => log.Should().Equal(["attempt", "attempt", "attempt", "action", "handler"], string.Join(",", log)));
    }

    public sealed record FailingCommand : ICommand<string>;

    public sealed record VoidFailingCommand : ICommand;

    public sealed record AmbiguousCommand : ICommand<string>;

    public sealed record CancelCommand : ICommand<string>;

    public sealed record RetryCommand : ICommand<string>;

    public sealed record ActionOnlyCommand : ICommand<string>;

    public sealed class FailingHandler(List<string> log) : ICommandHandler<FailingCommand, string> {
        public Task<string> HandleAsync(FailingCommand command, CancellationToken cancellationToken) {
            log.Add("attempt");

            throw new TimeoutException("timed out");
        }
    }

    public sealed class VoidFailingHandler(List<string> log) : ICommandHandler<VoidFailingCommand> {
        public Task<Unit> HandleAsync(VoidFailingCommand command, CancellationToken cancellationToken) {
            log.Add("void-attempt");

            throw new TimeoutException("timed out");
        }
    }

    public sealed class AmbiguousFailingHandler(List<string> log) : ICommandHandler<AmbiguousCommand, string> {
        public Task<string> HandleAsync(AmbiguousCommand command, CancellationToken cancellationToken) {
            log.Add("attempt");

            throw new InvalidOperationException("ambiguous");
        }
    }

    public sealed class CancelHandler : ICommandHandler<CancelCommand, string> {
        public Task<string> HandleAsync(CancelCommand command, CancellationToken cancellationToken) =>
            throw new OperationCanceledException(cancellationToken);
    }

    public sealed class AlwaysFailingHandler(List<string> log) : ICommandHandler<RetryCommand, string> {
        public Task<string> HandleAsync(RetryCommand command, CancellationToken cancellationToken) {
            log.Add("attempt");

            throw new TimeoutException("timed out");
        }
    }

    public sealed class ActionOnlyHandler(List<string> log) : ICommandHandler<ActionOnlyCommand, string> {
        public Task<string> HandleAsync(ActionOnlyCommand command, CancellationToken cancellationToken) {
            log.Add("attempt");

            throw new TimeoutException("timed out");
        }
    }

    public sealed class TimeoutFallback(List<string> log) : ExceptionHandler<FailingCommand, string, TimeoutException> {
        protected override Task<ExceptionHandlingResult<string>> HandleCoreAsync(FailingCommand request, TimeoutException exception, CancellationToken cancellationToken) {
            log.Add("handled");

            return Task.FromResult(ExceptionHandlingResult<string>.Handled("fallback"));
        }
    }

    public sealed class VoidTimeoutFallback(List<string> log) : ExceptionHandler<VoidFailingCommand, Unit, TimeoutException> {
        protected override Task<ExceptionHandlingResult<Unit>> HandleCoreAsync(VoidFailingCommand request, TimeoutException exception, CancellationToken cancellationToken) {
            log.Add("void-handled");

            return Task.FromResult(ExceptionHandlingResult<Unit>.Handled(Unit.Value));
        }
    }

    public sealed class BaseExceptionFallback(List<string> log) : ExceptionHandler<AmbiguousCommand, string, Exception> {
        protected override Task<ExceptionHandlingResult<string>> HandleCoreAsync(AmbiguousCommand request, Exception exception, CancellationToken cancellationToken) {
            log.Add("base");

            return Task.FromResult(ExceptionHandlingResult<string>.Handled("base"));
        }
    }

    public sealed class SpecificExceptionFallback(List<string> log) : ExceptionHandler<AmbiguousCommand, string, InvalidOperationException> {
        protected override Task<ExceptionHandlingResult<string>> HandleCoreAsync(AmbiguousCommand request, InvalidOperationException exception, CancellationToken cancellationToken) {
            log.Add("specific");

            return Task.FromResult(ExceptionHandlingResult<string>.Handled("specific"));
        }
    }

    public sealed class CancelledFallback(List<string> log) : ExceptionHandler<CancelCommand, string, OperationCanceledException> {
        protected override Task<ExceptionHandlingResult<string>> HandleCoreAsync(CancelCommand request, OperationCanceledException exception, CancellationToken cancellationToken) {
            log.Add("cancel-handled");

            return Task.FromResult(ExceptionHandlingResult<string>.Handled("cancelled"));
        }
    }

    public sealed class RetryAction(List<string> log) : ExceptionAction<RetryCommand, TimeoutException> {
        protected override Task HandleCoreAsync(RetryCommand request, TimeoutException exception, CancellationToken cancellationToken) {
            log.Add("action");

            return Task.CompletedTask;
        }
    }

    public sealed class RetryFallback(List<string> log) : ExceptionHandler<RetryCommand, string, TimeoutException> {
        protected override Task<ExceptionHandlingResult<string>> HandleCoreAsync(RetryCommand request, TimeoutException exception, CancellationToken cancellationToken) {
            log.Add("handler");

            return Task.FromResult(ExceptionHandlingResult<string>.Handled("handled-by-retry"));
        }
    }

    public sealed class ActionOnlyObserver(List<string> log) : ExceptionAction<ActionOnlyCommand, TimeoutException> {
        protected override Task HandleCoreAsync(ActionOnlyCommand request, TimeoutException exception, CancellationToken cancellationToken) {
            log.Add("observed");

            return Task.CompletedTask;
        }
    }
}
