//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Interfaces;

/// <summary>
/// Runs custom logic immediately after the pipeline behaviors and handler.
/// </summary>
/// <typeparam name="TRequest">Type of command or query.</typeparam>
/// <typeparam name="TResponse">Type of the result.</typeparam>
public interface IPostProcessor<in TRequest, in TResponse> {
    /// <summary>
    /// Execute logic after handling <paramref name="request"/>.
    /// </summary>
    Task ProcessAsync(TRequest request, TResponse response, CancellationToken cancellationToken = default);
}
