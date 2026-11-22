//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Recurring;

/// <summary>
/// Schedules recurring commands on the durable deferred store. Each occurrence re-stages the
/// next before executing, so deadlines survive restarts; <see cref="RecurringScheduleOptions.UniqueKey"/>
/// deduplicates schedules across callers/instances.
/// </summary>
public interface IRecurringDispatcher {
    /// <summary>
    /// Schedules <paramref name="command"/> at the next cron occurrence and every occurrence
    /// after it. Calling again with the same unique key is a no-op while an occurrence is
    /// pending.
    /// </summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <param name="command">The command to run.</param>
    /// <param name="cron">The five-field cron expression.</param>
    /// <param name="options">Schedule options; null means UTC, Skip, and the command type as key.</param>
    /// <param name="cancellationToken">Cancels staging.</param>
    /// <returns>The unique schedule key.</returns>
    Task<string> ScheduleAsync<TCommand>(
        TCommand command,
        CronExpression cron,
        RecurringScheduleOptions? options = null,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand;

    /// <summary>
    /// Removes every pending occurrence for <paramref name="uniqueKey"/>.
    /// </summary>
    /// <param name="uniqueKey">The schedule key returned by <see cref="ScheduleAsync{TCommand}"/>.</param>
    /// <param name="cancellationToken">Cancels removal.</param>
    /// <returns><see langword="true"/> when at least one pending occurrence was removed.</returns>
    Task<bool> RemoveAsync(string uniqueKey, CancellationToken cancellationToken = default);
}
