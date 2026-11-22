//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Interfaces;

/// <summary>
///  Dispatches queries through the pipeline and returns a result.
/// </summary>
public interface IQueryDispatcher {
    /// <summary>
    ///  Sends the specified query to its handler pipeline and returns the response.
    /// </summary>
    /// <typeparam name="TQuery">Type of query</typeparam>
    /// <typeparam name="TResult">Expected response type</typeparam>
    /// <param name="query">Query instance</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None"/>.</param>
    /// <returns>Task yielding the query response</returns>
    Task<TResult> DispatchAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken = default) where TQuery : IQuery<TResult>;

    /// <summary>
    /// Streams the specified query to its stream handler and returns the items lazily.
    /// Pipeline behaviors do not apply; see the remarks on the implementation.
    /// </summary>
    /// <typeparam name="TQuery">Type of stream query</typeparam>
    /// <typeparam name="TItem">Streamed item type</typeparam>
    /// <param name="query">Query instance</param>
    /// <param name="cancellationToken">A token the handler should observe cooperatively.</param>
    /// <returns>The lazily evaluated item sequence.</returns>
    IAsyncEnumerable<TItem> StreamAsync<TQuery, TItem>(TQuery query, CancellationToken cancellationToken = default)
        where TQuery : IStreamQuery<TItem>;
}
