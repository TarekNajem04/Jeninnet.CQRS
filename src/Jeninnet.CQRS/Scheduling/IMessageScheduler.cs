//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Scheduling;

/// <summary>
/// Transport-agnostic persistent scheduler. Delayed and one-shot messages fire once; recurring
/// messages re-arm from their five-field cron expression after each successful publish.
/// </summary>
public interface IMessageScheduler {
    /// <summary>Schedules a message to fire after <paramref name="delay"/>.</summary>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <param name="message">The message instance.</param>
    /// <param name="delay">The delay; non-positive values are due immediately.</param>
    /// <param name="options">Optional key/time-zone options.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The stable schedule key.</returns>
    Task<string> ScheduleAsync<TMessage>(TMessage message, TimeSpan delay, ScheduledMessageOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Schedules a message to fire at an absolute UTC instant.</summary>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <param name="message">The message instance.</param>
    /// <param name="dueUtc">The due instant.</param>
    /// <param name="options">Optional key/time-zone options.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task<string> ScheduleAtAsync<TMessage>(TMessage message, DateTimeOffset dueUtc, ScheduledMessageOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Schedules a recurring cron message. The key is required and idempotent.</summary>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <param name="message">The message instance.</param>
    /// <param name="cron">The five-field cron expression.</param>
    /// <param name="key">The stable schedule key.</param>
    /// <param name="options">Optional time-zone options; the key argument wins.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task<string> ScheduleRecurringAsync<TMessage>(TMessage message, CronExpression cron, string key, ScheduledMessageOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Removes a schedule; a missing key returns false.</summary>
    /// <param name="key">The schedule key.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    Task<bool> CancelAsync(string key, CancellationToken cancellationToken = default);
}
