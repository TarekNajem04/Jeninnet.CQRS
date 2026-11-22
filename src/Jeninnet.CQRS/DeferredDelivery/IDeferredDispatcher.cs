//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.DeferredDelivery;

/// <summary>
/// Defers command execution to a later point in time, in-process and without a broker.
/// </summary>
public interface IDeferredDispatcher {
    /// <summary>
    /// Occurs when a deferred command throws instead of completing. The pump isolates
    /// failures: one failing command never stops later ones.
    /// </summary>
    event EventHandler<DeferredFaultEventArgs>? Faulted;

    /// <summary>
    /// Schedules <paramref name="command"/> for execution after <paramref name="delay"/>.
    /// Fire-and-forget: the caller does not observe the outcome (see <see cref="Faulted"/>).
    /// </summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <param name="command">The command to run later. Never <see langword="null"/>.</param>
    /// <param name="delay">How long to wait before running. Non-positive values run on the next pump iteration.</param>
    /// <param name="cancellationToken">Cancels the deferral: a cancelled command never runs.</param>
    Task DeferAsync<TCommand>(TCommand command, TimeSpan delay, CancellationToken cancellationToken = default)
        where TCommand : ICommand;

    /// <summary>
    /// Schedules <paramref name="command"/> for execution after <paramref name="delay"/>.
    /// Fire-and-forget: the produced value is discarded.
    /// </summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <typeparam name="TResult">The (discarded) result type.</typeparam>
    /// <param name="command">The command to run later. Never <see langword="null"/>.</param>
    /// <param name="delay">How long to wait before running. Non-positive values run on the next pump iteration.</param>
    /// <param name="cancellationToken">Cancels the deferral: a cancelled command never runs.</param>
    Task DeferAsync<TCommand, TResult>(TCommand command, TimeSpan delay, CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult>;

    /// <summary>
    /// Re-stages the dead-lettered message named by <paramref name="deadLetterKey"/> and queues
    /// it to run immediately. The dead-letter entry is removed after the replayed execution
    /// succeeds; if it fails again, the entry stays and its attempt count is incremented.
    /// </summary>
    /// <param name="deadLetterKey">The key returned when the failure was recorded.</param>
    /// <param name="cancellationToken">Cancels staging.</param>
    /// <returns>
    /// <see langword="true"/> when the entry was found and re-staged; <see langword="false"/>
    /// when the key is unknown.
    /// </returns>
    Task<bool> ReplayAsync(string deadLetterKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues an envelope that is already persisted under <paramref name="key"/> (for example a
    /// recurring occurrence staged by a store). No-op until the pump starts; recovery would pick
    /// the entry up otherwise.
    /// </summary>
    /// <param name="key">The store key.</param>
    /// <param name="envelope">The stored envelope.</param>
    /// <param name="cancellationToken">Cancels the queued execution.</param>
    Task EnqueueAsync(string key, IDeferredEnvelope envelope, CancellationToken cancellationToken = default);
}