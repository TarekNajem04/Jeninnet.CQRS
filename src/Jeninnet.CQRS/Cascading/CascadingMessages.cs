//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Cascading;

/// <summary>
/// One deferred follow-up produced by a handler: the message type plus the statically bound
/// dispatch closure. Closures are created at the call site, so cascades never use reflection.
/// </summary>
/// <param name="MessageType">The runtime type of the follow-up message.</param>
/// <param name="Dispatch">Dispatches the follow-up through the broker.</param>
public sealed record CascadeStep(Type MessageType, Func<ICqrsBroker, CancellationToken, Task> Dispatch) {
    /// <summary>Gets the runtime type of the follow-up message.</summary>
    public Type MessageType { get; } = MessageType ?? throw new ArgumentNullException(nameof(MessageType));

    /// <summary>Gets the dispatch closure.</summary>
    public Func<ICqrsBroker, CancellationToken, Task> Dispatch { get; } = Dispatch ?? throw new ArgumentNullException(nameof(Dispatch));
}

/// <summary>
/// A result type that carries follow-up messages to dispatch once the pipeline (and therefore
/// any transaction) has completed.
/// </summary>
public interface ICascadingMessages {
    /// <summary>Gets the follow-up messages, in execution order.</summary>
    IReadOnlyList<CascadeStep> Steps { get; }
}

/// <summary>
/// Options controlling cascade execution.
/// </summary>
public sealed class CascadeOptions {
    /// <summary>
    /// Gets or sets the maximum cascade depth (a cascade of a cascade of ...).
    /// Defaults to 16; exceeding it throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public int MaxDepth { get; set; } = 16;
}

/// <summary>
/// Follow-up messages for a void command: return this from an <c>ICommand&lt;CascadingMessages&gt;</c>
/// handler and the dispatcher executes every step after the pipeline completes.
/// </summary>
public sealed class CascadingMessages : CascadingMessagesBase;

/// <summary>
/// Follow-up messages plus the command's own result.
/// </summary>
/// <typeparam name="TResult">The command result type.</typeparam>
/// <param name="result">The value the command computed.</param>
public sealed class CascadingMessages<TResult>(TResult result) : CascadingMessagesBase {
    /// <summary>Gets the command result.</summary>
    public TResult Result { get; } = result;
}

/// <summary>
/// Shared builder for cascading results.
/// </summary>
public abstract class CascadingMessagesBase : ICascadingMessages {
    private readonly List<CascadeStep> _steps = [];

    /// <inheritdoc />
    public IReadOnlyList<CascadeStep> Steps => _steps;

    /// <summary>Gets the number of queued follow-ups.</summary>
    public int Count => _steps.Count;

    /// <summary>Queues a void command.</summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <param name="command">The command to run after the pipeline completes.</param>
    /// <returns>This instance, for chaining.</returns>
    public CascadingMessagesBase Add<TCommand>(TCommand command)
        where TCommand : ICommand {
        Guard.NotNull(command);

        _steps.Add(new CascadeStep(typeof(TCommand), (broker, token) => broker.DispatchAsync(command, token)));

        return this;
    }

    /// <summary>Queues a command whose result is discarded.</summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <typeparam name="TResult">The discarded result type.</typeparam>
    /// <param name="command">The command to run after the pipeline completes.</param>
    /// <returns>This instance, for chaining.</returns>
    public CascadingMessagesBase AddWithResult<TCommand, TResult>(TCommand command)
        where TCommand : ICommand<TResult> {
        Guard.NotNull(command);

        _steps.Add(new CascadeStep(typeof(TCommand), async (broker, token) => await broker.DispatchAsync<TCommand, TResult>(command, token).ConfigureAwait(false)));

        return this;
    }

    /// <summary>Queues a notification for publishing after the pipeline completes.</summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <param name="notification">The notification to publish.</param>
    /// <returns>This instance, for chaining.</returns>
    public CascadingMessagesBase Publish<TNotification>(TNotification notification)
        where TNotification : INotification {
        Guard.NotNull(notification);

        _steps.Add(new CascadeStep(typeof(TNotification), (broker, token) => broker.PublishAsync(notification, token)));

        return this;
    }
}
