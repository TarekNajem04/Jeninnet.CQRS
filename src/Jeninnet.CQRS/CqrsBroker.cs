//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS;

/// <summary>
/// Provides a unified entry point for dispatching commands and queries using the CQRS (Command Query Responsibility Segregation) pattern.
/// </summary>
/// <remarks>
/// The CqrsBroker coordinates command and query dispatching by delegating to the provided <see cref="ICommandDispatcher"/> and <see cref="IQueryDispatcher"/> implementations.
/// This class is typically used to abstract the underlying/ dispatch mechanisms, allowing consumers to interact with a single broker for both commands and queries.
/// Thread safety and behavior depend on the injected dispatcher implementations.
/// </remarks>
public sealed class CqrsBroker : ICqrsBroker {
    private readonly ICommandDispatcher _commands;
    private readonly IQueryDispatcher _queries;
    private readonly INotificationPublisher? _publisher;

    /// <summary>
    /// Initializes the broker with the command and query dispatchers and an optional publisher.
    /// </summary>
    /// <param name="commands">The command dispatcher.</param>
    /// <param name="queries">The query dispatcher.</param>
    /// <param name="publisher">The optional notification publisher.</param>
    public CqrsBroker(ICommandDispatcher commands, IQueryDispatcher queries, INotificationPublisher? publisher = null) => (_commands, _queries, _publisher) = (commands, queries, publisher);

    /// <inheritdoc />
    public Task<TResult> DispatchAsync<TCommand, TResult>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand<TResult> => _commands.DispatchAsync<TCommand, TResult>(command, cancellationToken);
    /// <inheritdoc />
    public Task DispatchAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand => _commands.DispatchAsync(command, cancellationToken);
    /// <inheritdoc />
    Task<TResult> IQueryDispatcher.DispatchAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken) => _queries.DispatchAsync<TQuery, TResult>(query, cancellationToken);
    /// <inheritdoc />
    IAsyncEnumerable<TItem> IQueryDispatcher.StreamAsync<TQuery, TItem>(TQuery query, CancellationToken cancellationToken) =>
        _queries.StreamAsync<TQuery, TItem>(query, cancellationToken);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Thrown when no <see cref="INotificationPublisher"/> is registered. Call
    /// <c>AddCqrsNotifications()</c> (or any <c>AddCqrs*</c> registration that includes it)
    /// to enable publishing.
    /// </exception>
    public Task PublishAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification =>
        _publisher is null
            ? throw new InvalidOperationException(
                $"No {nameof(INotificationPublisher)} is registered. Call AddCqrsNotifications() to enable PublishAsync.")
            : _publisher.PublishAsync(notification, cancellationToken);

    /// <inheritdoc />
    public Task<BatchDispatchResult<TResult>> DispatchBatchAsync<TCommand, TResult>(
        IReadOnlyList<TCommand> commands,
        BatchDispatchOptions? options = null,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult> {
        Guard.NotNull(commands);
        var degree = ValidateDegree(options, 1);

        return RunBatchAsync(
            commands,
            degree,
            (command, token) => _commands.DispatchAsync<TCommand, TResult>(command, token),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<BatchDispatchResult<Unit>> DispatchBatchAsync<TCommand>(
        IReadOnlyList<TCommand> commands,
        BatchDispatchOptions? options = null,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand {
        Guard.NotNull(commands);
        var degree = ValidateDegree(options, 1);

        return RunBatchAsync(
            commands,
            degree,
            async (command, token) => {
                await _commands.DispatchAsync(command, token).ConfigureAwait(false);

                return Unit.Value;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<BatchDispatchResult<TResult>> DispatchQueryBatchAsync<TQuery, TResult>(
        IReadOnlyList<TQuery> queries,
        BatchDispatchOptions? options = null,
        CancellationToken cancellationToken = default)
        where TQuery : IQuery<TResult> {
        Guard.NotNull(queries);
        var degree = ValidateDegree(options, BatchDispatchOptions.DefaultQueryDegreeOfParallelism);

        return RunBatchAsync(
            queries,
            degree,
            (query, token) => _queries.DispatchAsync<TQuery, TResult>(query, token),
            cancellationToken);
    }

    private static int ValidateDegree(BatchDispatchOptions? options, int defaultDegree) {
        var degree = options?.MaxDegreeOfParallelism ?? defaultDegree;

        return degree < 1
            ? throw new ArgumentOutOfRangeException(nameof(options), degree, "MaxDegreeOfParallelism must be at least 1.")
            : degree;
    }

    private static async Task<BatchDispatchResult<TResponse>> RunBatchAsync<TRequest, TResponse>(
        IReadOnlyList<TRequest> requests,
        int degree,
        Func<TRequest, CancellationToken, Task<TResponse>> dispatch,
        CancellationToken cancellationToken) {
        var items = new BatchDispatchItem<TResponse>[requests.Count];

        if(degree is 1) {
            for(var index = 0; index < requests.Count; index++) {
                items[index] = await DispatchItemAsync(requests[index], index, dispatch, cancellationToken).ConfigureAwait(false);
            }
        } else {
            using var gate = new SemaphoreSlim(degree, degree);
            var workers = Enumerable.Range(0, requests.Count)
                .Select(async index => {
                    await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

                    try {
                        items[index] = await DispatchItemAsync(requests[index], index, dispatch, cancellationToken).ConfigureAwait(false);
                    }
                    finally {
                        gate.Release();
                    }
                });

            await Task.WhenAll(workers).ConfigureAwait(false);
        }

        return new BatchDispatchResult<TResponse>(items);
    }

    private static async Task<BatchDispatchItem<TResponse>> DispatchItemAsync<TRequest, TResponse>(
        TRequest request,
        int index,
        Func<TRequest, CancellationToken, Task<TResponse>> dispatch,
        CancellationToken cancellationToken) {
        try {
            var value = await dispatch(request, cancellationToken).ConfigureAwait(false);

            return BatchDispatchItem<TResponse>.Success(index, value);
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch(Exception exception) {
            return BatchDispatchItem<TResponse>.Failure(index, exception);
        }
    }
}
