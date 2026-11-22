//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// Characterization tests for CQRS-36 batch dispatch: every item runs, failures aggregate into
/// one exception without stopping the batch, sequential is the default, and bounded parallelism
/// honors its degree while preserving input order.
/// </summary>
[TestClass]
public class BatchDispatchTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task BatchOfTen_RunsEveryItem_InInputOrderAsync() {
        // Arrange
        var state = new BatchState();
        var broker = Build(state);
        var commands = Enumerable.Range(0, 10).Select(static index => new BatchCommand(index)).ToArray();

        // Act
        var result = await broker.DispatchBatchAsync<BatchCommand, int>(commands, cancellationToken: TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => result.IsSuccess.Should().BeTrue());
        MSTestAssertions.That(() => result.Items.Should().HaveCount(10));
        MSTestAssertions.That(() => result.Items.Select(static item => item.Value).Should().Equal(Enumerable.Range(0, 10)));
        MSTestAssertions.That(() => state.Executions.Should().Be(10));
    }

    [TestMethod]
    public async Task Failure_DoesNotStopTheBatch_AndAggregatesIntoOneExceptionAsync() {
        // Arrange
        var state = new BatchState();
        var broker = Build(state);
        var commands = Enumerable.Range(0, 5)
            .Select(index => new BatchCommand(index, Fail: index is 2))
            .ToArray();

        // Act
        var result = await broker.DispatchBatchAsync<BatchCommand, int>(commands, cancellationToken: TestContext!.CancellationToken);

        // Assert: all five ran; only index 2 failed.
        MSTestAssertions.That(() => state.Executions.Should().Be(5));
        MSTestAssertions.That(() => result.IsSuccess.Should().BeFalse());
        MSTestAssertions.That(() => result.Failures.Should().HaveCount(1));
        MSTestAssertions.That(() => result.Failures[0].Index.Should().Be(2));
        MSTestAssertions.That(() => result.Items[0].Value.Should().Be(0));
        MSTestAssertions.That(() => result.Items[4].Value.Should().Be(4));

        var thrown = Capture(() => result.ThrowIfFailed());
        MSTestAssertions.That(() => thrown.Should().BeOfType<BatchDispatchException>());
        MSTestAssertions.That(() => ((BatchDispatchException)thrown!).Failures.Should().HaveCount(1));
        MSTestAssertions.That(() => thrown!.Message.Should().Contain("1 item"));
    }

    [TestMethod]
    public async Task DefaultCommandBatch_IsSequentialAsync() {
        // Arrange
        var state = new BatchState { DelayMilliseconds = 5 };
        var broker = Build(state);
        var commands = Enumerable.Range(0, 6).Select(static index => new BatchCommand(index)).ToArray();

        // Act
        await broker.DispatchBatchAsync<BatchCommand, int>(commands, cancellationToken: TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => state.MaxActive.Should().Be(1));
    }

    [TestMethod]
    public async Task ParallelCommandBatch_HonorsDegree_AndPreservesInputOrderAsync() {
        // Arrange: three workers enter and wait for the gate, proving the degree.
        var state = new BatchState { GateAt = 3 };
        var broker = Build(state);
        var commands = Enumerable.Range(0, 9).Select(static index => new BatchCommand(index)).ToArray();
        var options = new BatchDispatchOptions { MaxDegreeOfParallelism = 3 };

        // Act
        var running = broker.DispatchBatchAsync<BatchCommand, int>(commands, options, TestContext!.CancellationToken);
        await state.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        state.Release.TrySetResult();
        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        MSTestAssertions.That(() => state.MaxActive.Should().Be(3));
        MSTestAssertions.That(() => result.Items.Select(static item => item.Value).Should().Equal(Enumerable.Range(0, 9)));
        MSTestAssertions.That(() => result.IsSuccess.Should().BeTrue());
    }

    [TestMethod]
    public async Task VoidCommandBatch_ReturnsOneUnitPerItemAsync() {
        // Arrange
        var state = new BatchState();
        var broker = Build(state);
        var commands = Enumerable.Range(0, 4).Select(static index => new BatchVoid(index)).ToArray();

        // Act
        var result = await broker.DispatchBatchAsync<BatchVoid>(commands, cancellationToken: TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => result.Items.Should().HaveCount(4));
        MSTestAssertions.That(() => result.Items.All(static item => item.IsSuccess).Should().BeTrue());
        MSTestAssertions.That(() => state.Executions.Should().Be(4));
    }

    [TestMethod]
    public async Task QueryBatch_UsesBoundedParallelism_AndPreservesInputOrderAsync() {
        // Arrange
        var state = new BatchState { GateAt = 4 };
        var broker = Build(state);
        var queries = Enumerable.Range(0, 8).Select(static index => new BatchQuery(index)).ToArray();
        var options = new BatchDispatchOptions { MaxDegreeOfParallelism = 4 };

        // Act
        var running = broker.DispatchQueryBatchAsync<BatchQuery, int>(queries, options, TestContext!.CancellationToken);
        await state.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        state.Release.TrySetResult();
        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        MSTestAssertions.That(() => state.MaxActive.Should().Be(4));
        MSTestAssertions.That(() => result.Items.Select(static item => item.Value).Should().Equal(Enumerable.Range(0, 8)));
    }

    [TestMethod]
    public async Task EmptyCommandBatch_ReturnsAnEmptySuccessfulResultAsync() {
        // Arrange
        var broker = Build(new BatchState());

        // Act
        var result = await broker.DispatchBatchAsync<BatchCommand, int>([], cancellationToken: TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => result.IsSuccess.Should().BeTrue());
        MSTestAssertions.That(() => result.Items.Should().BeEmpty());
    }

    [TestMethod]
    public async Task EmptyQueryBatch_ReturnsAnEmptySuccessfulResultAsync() {
        // Arrange
        var broker = Build(new BatchState());

        // Act
        var result = await broker.DispatchQueryBatchAsync<BatchQuery, int>([], cancellationToken: TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => result.IsSuccess.Should().BeTrue());
        MSTestAssertions.That(() => result.Items.Should().BeEmpty());
    }

    [TestMethod]
    public async Task InvalidQueryDegree_IsRejectedWithTheOptionNameAsync() {
        // Arrange
        var broker = Build(new BatchState());

        // Act
        var thrown = await CaptureAsync(() => broker.DispatchQueryBatchAsync<BatchQuery, int>(
            [new BatchQuery(0)],
            new BatchDispatchOptions { MaxDegreeOfParallelism = 0 },
            TestContext!.CancellationToken)).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<ArgumentOutOfRangeException>());
        MSTestAssertions.That(() => thrown!.Message.Should().Contain("MaxDegreeOfParallelism"));
    }

    [TestMethod]
    public async Task CancelledBatch_RethrowsOperationCancelledAsync() {
        // Arrange
        var broker = Build(new BatchState());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var thrown = await CaptureAsync(() => broker.DispatchBatchAsync<CancellableBatchCommand, int>(
            [new CancellableBatchCommand()],
            cancellationToken: cts.Token)).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<OperationCanceledException>());
    }

    [TestMethod]
    public async Task InvalidDegree_IsRejectedAsync() {
        // Arrange
        var broker = Build(new BatchState());
        var commands = new[] { new BatchCommand(0) };

        // Act
        var action = () => broker.DispatchBatchAsync<BatchCommand, int>(
            commands,
            new BatchDispatchOptions { MaxDegreeOfParallelism = 0 },
            TestContext!.CancellationToken);

        // Assert
        await MSTestAssertions.ThatAsync(() => action.Should().ThrowAsync<ArgumentOutOfRangeException>());
    }

    private static ICqrsBroker Build(BatchState state) {
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
        services.AddSingleton<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICommandHandler<BatchCommand, int>, BatchCommandHandler>();
        services.AddTransient<ICommandHandler<BatchVoid>, BatchVoidHandler>();
        services.AddTransient<ICommandHandler<CancellableBatchCommand, int>, CancellableBatchCommandHandler>();
        services.AddTransient<IQueryHandler<BatchQuery, int>, BatchQueryHandler>();
        services.AddSingleton<ICqrsBroker, CqrsBroker>();

        return services.BuildServiceProvider().GetRequiredService<ICqrsBroker>();
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

    private static Exception? Capture(Action action) {
        try {
            action();

            return null;
        }
        catch(Exception exception) {
            return exception;
        }
    }

    private sealed record BatchCommand(int Index, bool Fail = false) : ICommand<int>;

    private sealed record BatchVoid(int Index) : ICommand;

    private sealed record BatchQuery(int Index) : IQuery<int>;

    private sealed record CancellableBatchCommand : ICommand<int>;

    private sealed class CancellableBatchCommandHandler : ICommandHandler<CancellableBatchCommand, int> {
        public Task<int> HandleAsync(CancellableBatchCommand command, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(1);
        }
    }

    private sealed class BatchState {
        private readonly object _sync = new();
        private int _active;

        internal int Executions;
        internal int MaxActive { get; private set; }
        internal int DelayMilliseconds { get; init; }
        internal int GateAt { get; init; }
        internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal async Task EnterAsync() {
            lock(_sync) {
                _active++;

                if(_active > MaxActive) {
                    MaxActive = _active;
                }

                if(GateAt > 0 && _active >= GateAt) {
                    Reached.TrySetResult();
                }
            }

            if(DelayMilliseconds > 0) {
                await Task.Delay(DelayMilliseconds).ConfigureAwait(false);
            }

            if(GateAt > 0) {
                await Release.Task.ConfigureAwait(false);
            }
        }

        internal void Exit() {
            lock(_sync) {
                _active--;
            }
        }
    }

    private sealed class BatchCommandHandler(BatchState state) : ICommandHandler<BatchCommand, int> {
        public async Task<int> HandleAsync(BatchCommand command, CancellationToken cancellationToken) {
            Interlocked.Increment(ref state.Executions);
            await state.EnterAsync().ConfigureAwait(false);
            state.Exit();

            if(command.Fail) {
                throw new InvalidOperationException($"item {command.Index} failed");
            }

            return command.Index;
        }
    }

    private sealed class BatchVoidHandler(BatchState state) : ICommandHandler<BatchVoid> {
        public async Task<Unit> HandleAsync(BatchVoid command, CancellationToken cancellationToken) {
            Interlocked.Increment(ref state.Executions);
            await state.EnterAsync().ConfigureAwait(false);
            state.Exit();

            return Unit.Value;
        }
    }

    private sealed class BatchQueryHandler(BatchState state) : IQueryHandler<BatchQuery, int> {
        public async Task<int> HandleAsync(BatchQuery query, CancellationToken cancellationToken) {
            Interlocked.Increment(ref state.Executions);
            await state.EnterAsync().ConfigureAwait(false);
            state.Exit();

            return query.Index;
        }
    }
}