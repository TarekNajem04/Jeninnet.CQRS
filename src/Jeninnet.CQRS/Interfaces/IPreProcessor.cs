//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Interfaces;

/// <summary>
/// Runs custom logic immediately before the pipeline behaviors and handler.
/// </summary>
/// <typeparam name="TRequest">Type of command or query.</typeparam>
public interface IPreProcessor<in TRequest> {
    /// <summary>
    /// Execute logic before handling <paramref name="request"/>.
    /// </summary>
    Task ProcessAsync(TRequest request, CancellationToken cancellationToken = default);
}