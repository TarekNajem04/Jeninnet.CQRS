//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Interfaces;

/// <summary>
/// Handles a streaming query by producing an asynchronous sequence.
/// </summary>
/// <typeparam name="TQuery">The query type.</typeparam>
/// <typeparam name="TItem">The streamed item type.</typeparam>
public interface IStreamQueryHandler<in TQuery, TItem> where TQuery : IStreamQuery<TItem> { // NOSONAR: Variance would change the public interface contract; keep invariant.
    /// <summary>
    /// Handles the query and streams the results. Honor <paramref name="cancellationToken"/>
    /// cooperatively, ideally with an <c>[EnumeratorCancellation]</c> iterator parameter.
    /// </summary>
    /// <param name="query">The query to handle.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    IAsyncEnumerable<TItem> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
