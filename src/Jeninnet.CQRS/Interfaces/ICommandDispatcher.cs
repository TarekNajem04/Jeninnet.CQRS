//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Interfaces;

/// <summary>
/// Dispatches commands through the pipeline, returning no data.
/// </summary>
public interface ICommandDispatcher {
    /// <summary>
    /// Sends the specified command to its handler pipeline.
    /// </summary>
    /// <typeparam name="TCommand">Type of command</typeparam>
    /// <typeparam name="TResult">Expected response type</typeparam>
    /// <param name="command">Command instance</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None"/>.</param>
    /// <returns>Task yielding the command response</returns>
    Task<TResult> DispatchAsync<TCommand, TResult>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand<TResult>;

    /// <summary>
    /// Sends the specified command to its handler pipeline.
    /// </summary>
    /// <typeparam name="TCommand">Type of command</typeparam>
    /// <param name="command">Command instance</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None"/>.</param>
    /// <returns>Task that completes when handler finishes</returns>
    Task DispatchAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand;
}
