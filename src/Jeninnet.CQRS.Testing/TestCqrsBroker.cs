//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Testing;

/// <summary>
/// An <see cref="ICqrsBroker"/> decorator that records every dispatched command and query and
/// every published notification, then delegates to the wrapped broker. Construct it explicitly
/// in tests around a real broker: no hidden container magic.
/// </summary>
/// <remarks>
/// Initializes a new recording decorator over the given inner broker.
/// </remarks>
/// <param name="inner">The real broker doing the work. Never <see langword="null"/>.</param>
public sealed class TestCqrsBroker(ICqrsBroker inner) : ICqrsBroker {
    private readonly ICqrsBroker _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <summary>Gets the dispatched commands and command results requests, in order.</summary>
    public List<object> DispatchedCommands { get; } = [];

    /// <summary>Gets the dispatched queries, in order.</summary>
    public List<object> DispatchedQueries { get; } = [];

    /// <summary>Gets the published notifications, in order.</summary>
    public List<object> PublishedNotifications { get; } = [];

    /// <inheritdoc />
    public Task<TResult> DispatchAsync<TCommand, TResult>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult> {
        DispatchedCommands.Add(command);
        return _inner.DispatchAsync<TCommand, TResult>(command, cancellationToken);
    }

    /// <inheritdoc />
    public Task DispatchAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand {
        DispatchedCommands.Add(command);
        return _inner.DispatchAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    async Task<TResult> IQueryDispatcher.DispatchAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken) {
        DispatchedQueries.Add(query);
        return await ((IQueryDispatcher)_inner).DispatchAsync<TQuery, TResult>(query, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    IAsyncEnumerable<TItem> IQueryDispatcher.StreamAsync<TQuery, TItem>(TQuery query, CancellationToken cancellationToken) {
        DispatchedQueries.Add(query);
        return ((IQueryDispatcher)_inner).StreamAsync<TQuery, TItem>(query, cancellationToken);
    }

    /// <inheritdoc />
    public Task PublishAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification {
        PublishedNotifications.Add(notification);
        return _inner.PublishAsync(notification, cancellationToken);
    }

    /// <inheritdoc />
    public Task<BatchDispatchResult<TResult>> DispatchBatchAsync<TCommand, TResult>(
        IReadOnlyList<TCommand> commands,
        BatchDispatchOptions? options = null,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult> {
        Guard.NotNull(commands);

        foreach(var command in commands) {
            DispatchedCommands.Add(command);
        }

        return _inner.DispatchBatchAsync<TCommand, TResult>(commands, options, cancellationToken);
    }

    /// <inheritdoc />
    public Task<BatchDispatchResult<Unit>> DispatchBatchAsync<TCommand>(
        IReadOnlyList<TCommand> commands,
        BatchDispatchOptions? options = null,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand {
        Guard.NotNull(commands);

        foreach(var command in commands) {
            DispatchedCommands.Add(command);
        }

        return _inner.DispatchBatchAsync<TCommand>(commands, options, cancellationToken);
    }

    /// <inheritdoc />
    public Task<BatchDispatchResult<TResult>> DispatchQueryBatchAsync<TQuery, TResult>(
        IReadOnlyList<TQuery> queries,
        BatchDispatchOptions? options = null,
        CancellationToken cancellationToken = default)
        where TQuery : IQuery<TResult> {
        Guard.NotNull(queries);

        foreach(var query in queries) {
            DispatchedQueries.Add(query);
        }

        return _inner.DispatchQueryBatchAsync<TQuery, TResult>(queries, options, cancellationToken);
    }
}
