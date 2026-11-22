//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Testing;

/// <summary>
/// Captures every follow-up command and notification produced by a saga transition without
/// executing handlers. The harness clears it before each event.
/// </summary>
internal sealed class SagaRecordingBroker : ICqrsBroker {
    private readonly object _sync = new(); // NOSONAR: System.Threading.Lock is unavailable on netstandard2.0/net462; the object lock is the compatible shim.
    private readonly List<object> _messages = [];

    internal IReadOnlyList<object> Messages {
        get {
            lock(_sync) {
                return [.. _messages];
            }
        }
    }

    internal void Clear() {
        lock(_sync) {
            _messages.Clear();
        }
    }

    /// <inheritdoc />
    public Task<TResult> DispatchAsync<TCommand, TResult>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult> {
        Add(command);

        return Task.FromResult(default(TResult)!);
    }

    /// <inheritdoc />
    public Task DispatchAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand {
        Add(command);

        return Task.CompletedTask;
    }

    Task<TResult> IQueryDispatcher.DispatchAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken) {
        Add(query);

        return Task.FromResult(default(TResult)!);
    }

    IAsyncEnumerable<TItem> IQueryDispatcher.StreamAsync<TQuery, TItem>(TQuery query, CancellationToken cancellationToken) {
        Add(query);

        return Empty<TItem>();
    }

    /// <inheritdoc />
    public Task PublishAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification {
        Add(notification);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Batch.BatchDispatchResult<TResult>> DispatchBatchAsync<TCommand, TResult>(
        IReadOnlyList<TCommand> commands,
        Batch.BatchDispatchOptions? options = null,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult> =>
        throw new NotSupportedException("The saga test harness does not support batch dispatch.");

    /// <inheritdoc />
    public Task<Batch.BatchDispatchResult<Unit>> DispatchBatchAsync<TCommand>(
        IReadOnlyList<TCommand> commands,
        Batch.BatchDispatchOptions? options = null,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand =>
        throw new NotSupportedException("The saga test harness does not support batch dispatch.");

    /// <inheritdoc />
    public Task<Batch.BatchDispatchResult<TResult>> DispatchQueryBatchAsync<TQuery, TResult>(
        IReadOnlyList<TQuery> queries,
        Batch.BatchDispatchOptions? options = null,
        CancellationToken cancellationToken = default)
        where TQuery : IQuery<TResult> =>
        throw new NotSupportedException("The saga test harness does not support batch dispatch.");

    private void Add(object message) {
        lock(_sync) {
            _messages.Add(message);
        }
    }

    private static async IAsyncEnumerable<TItem> Empty<TItem>() {
        await Task.CompletedTask.ConfigureAwait(false);

        yield break;
    }
}
