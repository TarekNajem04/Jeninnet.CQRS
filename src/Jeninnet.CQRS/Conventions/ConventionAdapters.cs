//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Conventions;

/// <summary>
/// Adapts a convention-based void command handler to <see cref="ICommandHandler{TCommand}"/>.
/// </summary>
/// <remarks>Registered by the reflection scan only; generated registration emits direct adapters.</remarks>
/// <typeparam name="THandler">The convention handler type.</typeparam>
/// <typeparam name="TCommand">The command type.</typeparam>
[System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Reflection-registered convention handlers use expression trees.")]
[System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Reflection-registered convention handlers reflect over the handler type.")]
public sealed class ConventionCommandHandler<THandler, TCommand>(THandler handler) : ICommandHandler<TCommand>
    where THandler : class
    where TCommand : ICommand {
    private static readonly Func<THandler, TCommand, CancellationToken, Task> Invoker = ConventionHandlerInvoker.BindVoid<THandler, TCommand>();

    private readonly THandler _handler = handler ?? throw new ArgumentNullException(nameof(handler));

    /// <inheritdoc />
    public async Task<Unit> HandleAsync(TCommand request, CancellationToken cancellationToken = default) {
        await Invoker(_handler, request, cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }
}

/// <summary>
/// Adapts a convention-based result command handler to <see cref="ICommandHandler{TCommand, TResult}"/>.
/// </summary>
/// <typeparam name="THandler">The convention handler type.</typeparam>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResult">The result type.</typeparam>
[System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Reflection-registered convention handlers use expression trees.")]
[System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Reflection-registered convention handlers reflect over the handler type.")]
public sealed class ConventionCommandHandler<THandler, TCommand, TResult>(THandler handler) : ICommandHandler<TCommand, TResult> // NOSONAR: Generic adapter/state-machine arity is part of the public contract.
    where THandler : class
    where TCommand : ICommand<TResult> {
    private static readonly Func<THandler, TCommand, CancellationToken, Task<TResult>> Invoker = ConventionHandlerInvoker.BindResult<THandler, TCommand, TResult>();

    private readonly THandler _handler = handler ?? throw new ArgumentNullException(nameof(handler));

    /// <inheritdoc />
    public Task<TResult> HandleAsync(TCommand request, CancellationToken cancellationToken = default) =>
        Invoker(_handler, request, cancellationToken);
}

/// <summary>
/// Adapts a convention-based query handler to <see cref="IQueryHandler{TQuery, TResult}"/>.
/// </summary>
/// <typeparam name="THandler">The convention handler type.</typeparam>
/// <typeparam name="TQuery">The query type.</typeparam>
/// <typeparam name="TResult">The result type.</typeparam>
[System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Reflection-registered convention handlers use expression trees.")]
[System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Reflection-registered convention handlers reflect over the handler type.")]
public sealed class ConventionQueryHandler<THandler, TQuery, TResult>(THandler handler) : IQueryHandler<TQuery, TResult> // NOSONAR: Generic adapter/state-machine arity is part of the public contract.
    where THandler : class
    where TQuery : IQuery<TResult> {
    private static readonly Func<THandler, TQuery, CancellationToken, Task<TResult>> Invoker = ConventionHandlerInvoker.BindResult<THandler, TQuery, TResult>();

    private readonly THandler _handler = handler ?? throw new ArgumentNullException(nameof(handler));

    /// <inheritdoc />
    public Task<TResult> HandleAsync(TQuery request, CancellationToken cancellationToken = default) =>
        Invoker(_handler, request, cancellationToken);
}

/// <summary>
/// Adapts a convention-based notification handler to <see cref="INotificationHandler{TNotification}"/>.
/// </summary>
/// <typeparam name="THandler">The convention handler type.</typeparam>
/// <typeparam name="TNotification">The notification type.</typeparam>
[System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Reflection-registered convention handlers use expression trees.")]
[System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Reflection-registered convention handlers reflect over the handler type.")]
public sealed class ConventionNotificationHandler<THandler, TNotification>(THandler handler) : INotificationHandler<TNotification>
    where THandler : class
    where TNotification : INotification {
    private static readonly Func<THandler, TNotification, CancellationToken, Task> Invoker = ConventionHandlerInvoker.BindVoid<THandler, TNotification>();

    private readonly THandler _handler = handler ?? throw new ArgumentNullException(nameof(handler));

    /// <inheritdoc />
    public Task HandleAsync(TNotification notification, CancellationToken cancellationToken) =>
        Invoker(_handler, notification, cancellationToken);
}
