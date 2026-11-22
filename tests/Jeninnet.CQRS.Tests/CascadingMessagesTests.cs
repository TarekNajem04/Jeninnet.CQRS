//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// Characterization tests for CQRS-22: follow-up messages returned by a handler are dispatched
/// only after the pipeline (and any transaction) completes, with bounded depth and cycle checks.
/// </summary>
[TestClass]
public class CascadingMessagesTests {
    private static (ICqrsBroker Broker, List<string> Log) Build(bool transaction = false, int? maxDepth = null) {
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddTransient<ICommandHandler<OuterCommand, CascadingMessages>, OuterHandler>();
        services.AddTransient<ICommandHandler<WithResultCommand, CascadingMessages<int>>, WithResultHandler>();
        services.AddTransient<ICommandHandler<PublishCommand, CascadingMessages>, PublishHandler>();
        services.AddTransient<ICommandHandler<TransactionalCommand, CascadingMessages>, TransactionalHandler>();
        services.AddTransient<ICommandHandler<FailingCommand, CascadingMessages>, FailingHandler>();
        services.AddTransient<ICommandHandler<FollowA>, FollowAHandler>();
        services.AddTransient<ICommandHandler<FollowB>, FollowBHandler>();
        services.AddTransient<ICommandHandler<TrackedCommand, string>, TrackedHandler>();
        services.AddTransient<ICommandHandler<CycleCommand, CascadingMessages>, CycleHandler>();
        services.AddTransient<ICommandHandler<DepthA, CascadingMessages>, DepthAHandler>();
        services.AddTransient<ICommandHandler<DepthB, CascadingMessages>, DepthBHandler>();
        services.AddTransient<ICommandHandler<DepthC, CascadingMessages>, DepthCHandler>();
        services.AddTransient<INotificationHandler<SomethingHappened>, SomethingHappenedHandler>();
        services.AddCqrsNotifications();

        if(transaction) {
            services.AddSingleton(new RecordingTransactionScope(log));
            services.AddTransient<ITransactionScope>(static provider => provider.GetRequiredService<RecordingTransactionScope>());
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        }

        if(maxDepth is not null) {
            services.AddSingleton(new CascadeOptions { MaxDepth = maxDepth.Value });
        }

        return (services.BuildServiceProvider().GetRequiredService<ICqrsBroker>(), log);
    }

    [TestMethod]
    public async Task FollowUps_DispatchedInOrderAsync() {
        // Arrange
        var (broker, log) = Build();

        // Act
        var result = await broker.DispatchAsync<OuterCommand, CascadingMessages>(new OuterCommand(), default);

        // Assert: handler output then follow-ups, in the declared order.
        MSTestAssertions.That(() => log.Should().Equal(["handler:outer", "follow:A", "follow:B"]));
        MSTestAssertions.That(() => result.Count.Should().Be(2));
    }

    [TestMethod]
    public async Task FollowUps_RunAfterCommitAsync() {
        // Arrange
        var (broker, log) = Build(transaction: true);

        // Act
        await broker.DispatchAsync<TransactionalCommand, CascadingMessages>(new TransactionalCommand(), default);

        // Assert: the outer commit precedes the cascade; the cascade gets its own scope.
        var commitIndex = log.IndexOf("Commit");
        var cascadeIndex = log.IndexOf("follow:A");

        MSTestAssertions.That(() => log.Should().Equal(["Begin", "handler:transactional", "Commit", "Begin", "follow:A", "Commit"], string.Join(",", log)));
        MSTestAssertions.That(() => commitIndex.Should().BeLessThan(cascadeIndex));
    }

    [TestMethod]
    public async Task Rollback_DispatchesNothingAsync() {
        // Arrange
        var (broker, log) = Build(transaction: true);

        // Act
        var thrown = await CaptureAsync(() => broker.DispatchAsync<FailingCommand, CascadingMessages>(new FailingCommand(), default));

        // Assert: rollback happened and no follow-up ran.
        MSTestAssertions.That(() => thrown.Should().BeOfType<InvalidOperationException>());
        MSTestAssertions.That(() => log.Should().Equal(["Begin", "handler:failing", "Rollback"]));
    }

    [TestMethod]
    public async Task NotificationCascade_IsPublishedAsync() {
        // Arrange
        var (broker, log) = Build();

        // Act
        await broker.DispatchAsync<PublishCommand, CascadingMessages>(new PublishCommand(), default);

        // Assert
        MSTestAssertions.That(() => log.Should().Equal(["handler:publish", "notified"]));
    }

    [TestMethod]
    public async Task ResultAndFollowUp_AreBothAvailableAsync() {
        // Arrange
        var (broker, log) = Build();

        // Act
        var result = await broker.DispatchAsync<WithResultCommand, CascadingMessages<int>>(new WithResultCommand(), default);

        // Assert: the caller keeps its result; the result-bearing follow-up ran and was discarded.
        MSTestAssertions.That(() => result.Result.Should().Be(42));
        MSTestAssertions.That(() => log.Should().Equal(["handler:with-result", "tracked"]));
    }

    [TestMethod]
    public async Task Cycle_IsDetectedAsync() {
        // Arrange
        var (broker, log) = Build();

        // Act
        var thrown = await CaptureAsync(() => broker.DispatchAsync<CycleCommand, CascadingMessages>(new CycleCommand(), default));

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<InvalidOperationException>());
        MSTestAssertions.That(() => thrown!.Message.Should().Contain("cycle"));
        MSTestAssertions.That(() => log.Should().Equal(["handler:cycle"]));
    }

    [TestMethod]
    public async Task Depth_IsBoundedAsync() {
        // Arrange: A -> B -> C with a maximum of 2.
        var (broker, log) = Build(maxDepth: 2);

        // Act
        var thrown = await CaptureAsync(() => broker.DispatchAsync<DepthA, CascadingMessages>(new DepthA(), default));

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<InvalidOperationException>());
        MSTestAssertions.That(() => thrown!.Message.Should().Contain("depth"));
        MSTestAssertions.That(() => log.Should().Equal(["depth:A", "depth:B"]));
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

    public sealed record OuterCommand : ICommand<CascadingMessages>;

    public sealed record WithResultCommand : ICommand<CascadingMessages<int>>;

    public sealed record PublishCommand : ICommand<CascadingMessages>;

    public sealed record TransactionalCommand : ICommand<CascadingMessages>;

    public sealed record FailingCommand : ICommand<CascadingMessages>;

    public sealed record FollowA : ICommand;

    public sealed record FollowB : ICommand;

    public sealed record TrackedCommand : ICommand<string>;

    public sealed record CycleCommand : ICommand<CascadingMessages>;

    public sealed record DepthA : ICommand<CascadingMessages>;

    public sealed record DepthB : ICommand<CascadingMessages>;

    public sealed record DepthC : ICommand<CascadingMessages>;

    public sealed record SomethingHappened : INotification;

    public sealed class OuterHandler(List<string> log) : ICommandHandler<OuterCommand, CascadingMessages> {
        public Task<CascadingMessages> HandleAsync(OuterCommand command, CancellationToken cancellationToken) {
            log.Add("handler:outer");

            return Task.FromResult(new CascadingMessages()
                .Add(new FollowA())
                .Add(new FollowB()) as CascadingMessages)!;
        }
    }

    public sealed class WithResultHandler(List<string> log) : ICommandHandler<WithResultCommand, CascadingMessages<int>> {
        public Task<CascadingMessages<int>> HandleAsync(WithResultCommand command, CancellationToken cancellationToken) {
            log.Add("handler:with-result");

            var cascades = new CascadingMessages<int>(42);
            cascades.AddWithResult<TrackedCommand, string>(new TrackedCommand());

            return Task.FromResult(cascades);
        }
    }

    public sealed class PublishHandler(List<string> log) : ICommandHandler<PublishCommand, CascadingMessages> {
        public Task<CascadingMessages> HandleAsync(PublishCommand command, CancellationToken cancellationToken) {
            log.Add("handler:publish");

            return Task.FromResult(new CascadingMessages().Publish(new SomethingHappened()) as CascadingMessages)!;
        }
    }

    public sealed class TransactionalHandler(List<string> log) : ICommandHandler<TransactionalCommand, CascadingMessages> {
        public Task<CascadingMessages> HandleAsync(TransactionalCommand command, CancellationToken cancellationToken) {
            log.Add("handler:transactional");

            return Task.FromResult(new CascadingMessages().Add(new FollowA()) as CascadingMessages)!;
        }
    }

    public sealed class FailingHandler(List<string> log) : ICommandHandler<FailingCommand, CascadingMessages> {
        public Task<CascadingMessages> HandleAsync(FailingCommand command, CancellationToken cancellationToken) {
            log.Add("handler:failing");

            throw new InvalidOperationException("boom");
        }
    }

    public sealed class FollowAHandler(List<string> log) : ICommandHandler<FollowA> {
        public Task<Unit> HandleAsync(FollowA command, CancellationToken cancellationToken) {
            log.Add("follow:A");

            return Task.FromResult(Unit.Value);
        }
    }

    public sealed class FollowBHandler(List<string> log) : ICommandHandler<FollowB> {
        public Task<Unit> HandleAsync(FollowB command, CancellationToken cancellationToken) {
            log.Add("follow:B");

            return Task.FromResult(Unit.Value);
        }
    }

    public sealed class TrackedHandler(List<string> log) : ICommandHandler<TrackedCommand, string> {
        public Task<string> HandleAsync(TrackedCommand command, CancellationToken cancellationToken) {
            log.Add("tracked");

            return Task.FromResult("tracked");
        }
    }

    public sealed class CycleHandler(List<string> log) : ICommandHandler<CycleCommand, CascadingMessages> {
        public Task<CascadingMessages> HandleAsync(CycleCommand command, CancellationToken cancellationToken) {
            log.Add("handler:cycle");

            var cascades = new CascadingMessages();
            cascades.AddWithResult<CycleCommand, CascadingMessages>(new CycleCommand());

            return Task.FromResult(cascades);
        }
    }

    public sealed class DepthAHandler(List<string> log) : ICommandHandler<DepthA, CascadingMessages> {
        public Task<CascadingMessages> HandleAsync(DepthA command, CancellationToken cancellationToken) {
            log.Add("depth:A");

            var cascades = new CascadingMessages();
            cascades.AddWithResult<DepthB, CascadingMessages>(new DepthB());

            return Task.FromResult(cascades);
        }
    }

    public sealed class DepthBHandler(List<string> log) : ICommandHandler<DepthB, CascadingMessages> {
        public Task<CascadingMessages> HandleAsync(DepthB command, CancellationToken cancellationToken) {
            log.Add("depth:B");

            var cascades = new CascadingMessages();
            cascades.AddWithResult<DepthC, CascadingMessages>(new DepthC());

            return Task.FromResult(cascades);
        }
    }

    public sealed class DepthCHandler(List<string> log) : ICommandHandler<DepthC, CascadingMessages> {
        public Task<CascadingMessages> HandleAsync(DepthC command, CancellationToken cancellationToken) {
            log.Add("depth:C");

            return Task.FromResult(new CascadingMessages());
        }
    }

    public sealed class SomethingHappenedHandler(List<string> log) : INotificationHandler<SomethingHappened> {
        public Task HandleAsync(SomethingHappened notification, CancellationToken cancellationToken) {
            log.Add("notified");

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingTransactionScope(List<string> log) : ITransactionScope {
        public Task BeginAsync(CancellationToken cancellationToken) {
            log.Add("Begin");

            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken cancellationToken) {
            log.Add("Commit");

            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken) {
            log.Add("Rollback");

            return Task.CompletedTask;
        }
    }
}