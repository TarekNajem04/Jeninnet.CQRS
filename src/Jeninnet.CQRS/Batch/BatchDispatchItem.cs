//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Batch;

/// <summary>
/// The outcome of one item in a batch dispatch.
/// </summary>
/// <typeparam name="TResponse">The item response type.</typeparam>
/// <param name="Index">The zero-based input index.</param>
/// <param name="Value">The response when the item succeeded.</param>
/// <param name="Error">The exception when the item failed.</param>
public sealed record BatchDispatchItem<TResponse>(int Index, TResponse? Value, Exception? Error) {
    /// <summary>Gets a value indicating whether the item succeeded.</summary>
    public bool IsSuccess => Error is null;

    internal static BatchDispatchItem<TResponse> Success(int index, TResponse value) => new(index, value, null);

    internal static BatchDispatchItem<TResponse> Failure(int index, Exception error) => new(index, default, error);
}