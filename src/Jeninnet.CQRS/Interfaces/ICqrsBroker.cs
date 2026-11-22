//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Interfaces;

/// <summary>
/// High‐level façade combining command and query dispatchers.
/// </summary>
public interface ICqrsBroker : ICommandDispatcher, IQueryDispatcher {
    /// <summary>
    /// Publishes a notification to every registered handler.
    /// </summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <param name="notification">The notification to publish.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    Task PublishAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;

    /// <summary>
    /// Dispatches commands that return a result. Sequential by default; set
    /// <see cref="Batch.BatchDispatchOptions.MaxDegreeOfParallelism"/> for bounded parallelism.
    /// Per-item failures are values; call <see cref="Batch.BatchDispatchResult{TResponse}.ThrowIfFailed"/>
    /// for the aggregated exception.
    /// </summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="commands">Commands in input order.</param>
    /// <param name="options">Batch scheduling options; null means sequential.</param>
    /// <param name="cancellationToken">Cancels the remaining items.</param>
    Task<Batch.BatchDispatchResult<TResult>> DispatchBatchAsync<TCommand, TResult>(
        IReadOnlyList<TCommand> commands,
        Batch.BatchDispatchOptions? options = null,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult>;

    /// <summary>
    /// Dispatches void commands in a batch, returning <see cref="Unit"/> per item.
    /// Sequential by default.
    /// </summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <param name="commands">Commands in input order.</param>
    /// <param name="options">Batch scheduling options; null means sequential.</param>
    /// <param name="cancellationToken">Cancels the remaining items.</param>
    Task<Batch.BatchDispatchResult<Unit>> DispatchBatchAsync<TCommand>(
        IReadOnlyList<TCommand> commands,
        Batch.BatchDispatchOptions? options = null,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand;

    /// <summary>
    /// Dispatches queries with bounded parallelism (processor count capped at 8 unless options
    /// override it). Results preserve input order; failures are per-item values.
    /// </summary>
    /// <typeparam name="TQuery">The query type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="queries">Queries in input order.</param>
    /// <param name="options">Batch scheduling options; null uses the bounded query default.</param>
    /// <param name="cancellationToken">Cancels the remaining items.</param>
    Task<Batch.BatchDispatchResult<TResult>> DispatchQueryBatchAsync<TQuery, TResult>(
        IReadOnlyList<TQuery> queries,
        Batch.BatchDispatchOptions? options = null,
        CancellationToken cancellationToken = default)
        where TQuery : IQuery<TResult>;
}
