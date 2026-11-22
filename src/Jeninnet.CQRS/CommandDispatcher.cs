//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS;

/// <summary>
/// A lightweight dispatcher that dispatches commands through the built-in .NET DI container and invokes any registered pipeline behaviors.
/// </summary>
/// <remarks>
/// Resolves <see cref="ICommandHandler{TCommand}"/> or <see cref="IQueryHandler{TQuery, TResult}"/> along with any <see cref="IPipelineBehavior{TRequest, TResponse}"/> for the given request type.
/// Wraps void‐returning command handlers in a <see cref="Unit"/> result so that the same pipeline mechanism can be used for both commands and queries.
/// </remarks>
public sealed class CommandDispatcher(IServiceProvider provider) : DispatcherBase(provider), ICommandDispatcher {
    /// <inheritdoc />
    /// <remarks>
    /// When the handler returns <see cref="ICascadingMessages"/>, the queued follow-ups are
    /// dispatched only after the whole pipeline (including the transaction behavior) completes,
    /// so a rolled-back transaction never cascades.
    /// </remarks>
    public async Task<TResult> DispatchAsync<TCommand, TResult>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand<TResult> {
        var result = await ExecuteAsync<ICommandHandler<TCommand, TResult>, TCommand, TResult>(command, cancellationToken).ConfigureAwait(false);
        await CascadeRunner.RunAsync(result, typeof(TCommand), Provider, cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Resolves the exact <c>ICommandHandler&lt;TCommand, Unit&gt;</c> registration first;
    /// when only the ergonomic one-generic <c>ICommandHandler&lt;TCommand&gt;</c> form is
    /// registered, it is used as the fallback (last registration still wins within each form).
    /// </remarks>
    public Task DispatchAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand {
        Guard.NotNull(command);

        IHandler<TCommand, Unit>? handler = Provider.GetService<ICommandHandler<TCommand, Unit>>()
            ?? Provider.GetService<ICommandHandler<TCommand>>();

        return handler is null
            ? throw new InvalidOperationException($"No handler registered for {typeof(TCommand).Name}")
            : ExecuteWithExceptionPolicyAsync(handler, command, cancellationToken);
    }
}