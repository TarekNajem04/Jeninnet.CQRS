//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Batch;

/// <summary>
/// The complete result of a batch dispatch. Items are always in input order, regardless of the
/// degree of parallelism; failures are values, never swallowed or reordered.
/// </summary>
/// <typeparam name="TResponse">The item response type.</typeparam>
public sealed class BatchDispatchResult<TResponse> {
    private readonly BatchDispatchItem<TResponse>[] _items;
    private IReadOnlyList<BatchDispatchItem<TResponse>>? _failures;

    internal BatchDispatchResult(BatchDispatchItem<TResponse>[] items) {
        _items = items ?? throw new ArgumentNullException(nameof(items));
    }

    /// <summary>Gets every item result in input order.</summary>
    public IReadOnlyList<BatchDispatchItem<TResponse>> Items => _items;

    /// <summary>Gets a value indicating whether every item succeeded.</summary>
    public bool IsSuccess => _items.All(static item => item.IsSuccess);

    /// <summary>Gets the failed items in input order.</summary>
    public IReadOnlyList<BatchDispatchItem<TResponse>> Failures =>
        _failures ??= [.. _items.Where(static item => !item.IsSuccess)];

    /// <summary>
    /// Throws one <see cref="BatchDispatchException"/> carrying every failure when at least one
    /// item failed; otherwise returns normally.
    /// </summary>
    /// <exception cref="BatchDispatchException">At least one item failed.</exception>
    public void ThrowIfFailed() {
        if(!IsSuccess) {
            var failures = Failures
                .Select(static item => new BatchDispatchFailure(item.Index, item.Error!))
                .ToList();

            throw new BatchDispatchException(failures);
        }
    }
}
