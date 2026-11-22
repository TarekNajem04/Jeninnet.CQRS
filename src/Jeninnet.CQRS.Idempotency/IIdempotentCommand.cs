//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency;

/// <summary>
/// A marker interface for commands whose results should be cached and replayed (idempotent) when the same <see cref="IdempotencyKey"/> is used more than once.
/// </summary>
/// <typeparam name="TResponse">The type of response produced by the command.</typeparam>
public interface IIdempotentCommand<out TResponse> : ICommand<TResponse> {
    /// <summary>
    /// A unique key provided by the caller to identify this command for de-duplication purposes.
    /// </summary>
    string IdempotencyKey { get; }
}
