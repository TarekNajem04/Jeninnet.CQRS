//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS;

/// <summary>
/// A lightweight dispatcher that dispatches queries through the built-in .NET DI container and invokes any registered pipeline behaviors.
/// </summary>
public sealed class QueryDispatcher(IServiceProvider provider) : DispatcherBase(provider), IQueryDispatcher {
    /// <inheritdoc />
    public Task<TResult> DispatchAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken = default) where TQuery : IQuery<TResult> =>
        ExecuteAsync<IQueryHandler<TQuery, TResult>, TQuery, TResult>(query, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Streaming bypasses the <see cref="IPipelineBehavior{TRequest, TResponse}"/> chain: behaviors
    /// are built around single <see cref="Task{TResult}"/> responses and cannot observe a lazy
    /// sequence. For retry/timeout semantics, materialize the stream inside a regular handler
    /// or wrap the call site.
    /// </remarks>
    public IAsyncEnumerable<TItem> StreamAsync<TQuery, TItem>(TQuery query, CancellationToken cancellationToken = default)
        where TQuery : IStreamQuery<TItem> {
        Guard.NotNull(query);

        var handler = Provider.GetService<IStreamQueryHandler<TQuery, TItem>>()
            ?? throw new InvalidOperationException($"No stream handler registered for {typeof(TQuery).Name}");

        return handler.HandleAsync(query, cancellationToken);
    }
}
