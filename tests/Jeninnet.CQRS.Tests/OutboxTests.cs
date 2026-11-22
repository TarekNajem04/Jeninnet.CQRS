//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// Characterization tests for CQRS-27: notifications staged inside a transaction are published
/// only after commit, a rollback publishes nothing, publish failures stay pending for the
/// dispatcher, and direct publishes remain immediate.
/// </summary>
[TestClass]
public class OutboxTests {
    private static (ICqrsBroker Broker, IOutboxStore Store, List<string> Log, NotificationSwitch Switch, IServiceProvider Provider) Build() {
        var log = new List<string>();
        var state = new NotificationSwitch();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddSingleton(state);
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddTransient<ICommandHandler<OutboxCommand>, OutboxCommandHandler>();
        services.AddTransient<INotificationHandler<OutboxTemplate>, RecordingNotificationHandler>();
        services.AddCqrsTransactionBehavior();
        services.AddTransient<ITransactionScope, RecordingTransactionScope>();
        services.AddCqrsNotificationOutbox();
        var provider = services.BuildServiceProvider();

        return (provider.GetRequiredService<ICqrsBroker>(), provider.GetRequiredService<IOutboxStore>(), log, state, provider);
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
    public async Task RolledBackTransaction_NeverPublishesAsync() {
        // Arrange
        var (broker, store, log, _, _) = Build();

        // Act: the handler publishes, then throws, so the transaction rolls back.
        var thrown = await CaptureAsync(() => broker.DispatchAsync(new OutboxCommand("lost", Fail: true), default));

        // Assert: the queued notification was discarded with the transaction.
        MSTestAssertions.That(() => thrown.Should().BeOfType<InvalidOperationException>());
        MSTestAssertions.That(() => log.Should().Equal(["begin", "rollback"]));
        MSTestAssertions.That(() => store.ListAsync(default).GetAwaiter().GetResult().Should().BeEmpty());
    }

    [TestMethod]
    public async Task CommittedTransaction_PublishesAfterCommitAsync() {
        // Arrange
        var (broker, store, log, _, _) = Build();

        // Act
        await broker.DispatchAsync(new OutboxCommand("kept", Fail: false), default);

        // Assert: commit precedes the publish, and the outbox drained.
        MSTestAssertions.That(() => log.Should().Equal(["begin", "commit", "publish:kept"]));
        MSTestAssertions.That(() => store.ListAsync(default).GetAwaiter().GetResult().Should().BeEmpty());
    }

    [TestMethod]
    public async Task PublishFailure_LeavesEntryPending_ThenDispatcherRetriesAsync() {
        // Arrange: the transaction commits, but the handler fails on first delivery.
        var (broker, store, log, state, provider) = Build();
        state.Fail = true;

        // Act: a publish failure after commit must not fail the command nor lose the message.
        await broker.DispatchAsync(new OutboxCommand("retry", Fail: false), default);

        // Assert
        MSTestAssertions.That(() => log.Should().Equal(["begin", "commit", "fail:retry"]));
        MSTestAssertions.That(() => store.ListAsync(default).GetAwaiter().GetResult().Should().HaveCount(1));

        state.Fail = false;
        var dispatcher = provider.GetServices<IHostedService>().OfType<OutboxDispatcher>().Single();
        var delivered = await dispatcher.DispatchPendingAsync(default);

        MSTestAssertions.That(() => delivered.Should().Be(1));
        MSTestAssertions.That(() => log.Should().Equal(["begin", "commit", "fail:retry", "publish:retry"]));
        MSTestAssertions.That(() => store.ListAsync(default).GetAwaiter().GetResult().Should().BeEmpty());
    }

    [TestMethod]
    public async Task PublishOutsideTransaction_IsImmediateAsync() {
        // Arrange
        var (_, store, log, _, provider) = Build();
        var publisher = provider.GetRequiredService<INotificationPublisher>();

        // Act
        await publisher.PublishAsync(new OutboxTemplate("direct"), default);

        // Assert: no transaction queued it; it was staged, published, and completed.
        MSTestAssertions.That(() => log.Should().Equal(["publish:direct"]));
        MSTestAssertions.That(() => store.ListAsync(default).GetAwaiter().GetResult().Should().BeEmpty());
    }

    [TestMethod]
    public async Task Dispatcher_RetriesUntilSuccess_WithoutDuplicatesAsync() {
        // Arrange: an entry that has survived a previous process.
        var (_, store, log, state, provider) = Build();
        var envelope = new OutboxNotification<OutboxTemplate>(new OutboxTemplate("pending"), TimeProvider.System.GetUtcNow());
        await store.StageAsync(envelope, default);
        var dispatcher = provider.GetServices<IHostedService>().OfType<OutboxDispatcher>().Single();
        state.Fail = true;

        // Act: first pass fails, second succeeds, third finds nothing.
        var first = await dispatcher.DispatchPendingAsync(default);
        state.Fail = false;
        var second = await dispatcher.DispatchPendingAsync(default);
        var third = await dispatcher.DispatchPendingAsync(default);

        // Assert
        MSTestAssertions.That(() => first.Should().Be(0));
        MSTestAssertions.That(() => second.Should().Be(1));
        MSTestAssertions.That(() => third.Should().Be(0));
        MSTestAssertions.That(() => log.Should().Equal(["fail:pending", "publish:pending"]));
        MSTestAssertions.That(() => store.ListAsync(default).GetAwaiter().GetResult().Should().BeEmpty());
    }

    private sealed class NotificationSwitch {
        public bool Fail { get; set; }
    }

    private sealed record OutboxCommand(string Name, bool Fail) : ICommand;

    private sealed record OutboxTemplate(string Name) : INotification;

    private sealed class OutboxCommandHandler(INotificationPublisher publisher) : ICommandHandler<OutboxCommand> {
        public async Task<Unit> HandleAsync(OutboxCommand command, CancellationToken cancellationToken) {
            await publisher.PublishAsync(new OutboxTemplate(command.Name), cancellationToken).ConfigureAwait(false);

            if(command.Fail) {
                throw new InvalidOperationException("handler failed");
            }

            return Unit.Value;
        }
    }

    private sealed class RecordingNotificationHandler(List<string> log, NotificationSwitch state) : INotificationHandler<OutboxTemplate> {
        public Task HandleAsync(OutboxTemplate notification, CancellationToken cancellationToken) {
            if(state.Fail) {
                log.Add($"fail:{notification.Name}");

                throw new InvalidOperationException("publish failed");
            }

            log.Add($"publish:{notification.Name}");

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingTransactionScope(List<string> log) : ITransactionScope {
        public Task BeginAsync(CancellationToken cancellationToken) {
            log.Add("begin");

            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken cancellationToken) {
            log.Add("commit");

            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken) {
            log.Add("rollback");

            return Task.CompletedTask;
        }
    }
}