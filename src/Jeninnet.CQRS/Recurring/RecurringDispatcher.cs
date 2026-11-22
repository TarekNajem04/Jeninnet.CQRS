//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Recurring;

/// <summary>
/// Default <see cref="IRecurringDispatcher"/>: schedules the first occurrence on the keyed
/// deferred store and delegates later occurrences to <see cref="RecurringScheduler"/>.
/// </summary>
/// <param name="store">The keyed durable deferred store. Never <see langword="null"/>.</param>
/// <param name="clock">The clock used for the first next-run. Never <see langword="null"/>.</param>
/// <param name="dispatcher">The deferred dispatcher used to queue staged occurrences. Never <see langword="null"/>.</param>
/// <param name="tenant">The ambient tenant adapter; <see langword="null"/> without tenancy.</param>
public sealed class RecurringDispatcher(IKeyedDeferredStore store, TimeProvider clock, IDeferredDispatcher dispatcher, TenantAccessor? tenant = null) : IRecurringDispatcher {
    private readonly IKeyedDeferredStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly IDeferredDispatcher _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    private readonly TenantAccessor? _tenant = tenant;

    /// <inheritdoc />
    public async Task<string> ScheduleAsync<TCommand>(
        TCommand command,
        CronExpression cron,
        RecurringScheduleOptions? options = null,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand {
        Guard.NotNull(command);
        Guard.NotNull(cron);

        options ??= new RecurringScheduleOptions();
        var uniqueKey = StringCompat.IsNullOrWhiteSpace(options.UniqueKey)
            ? (typeof(TCommand).FullName ?? typeof(TCommand).Name).Trim()
            : options.UniqueKey.Trim();

        var prefix = RecurringKeys.Prefix(uniqueKey);
        var pending = await _store.ListAsync(cancellationToken).ConfigureAwait(false);

        if(pending.Any(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal))) {
            return uniqueKey;
        }

        var nextDue = cron.GetNextOccurrence(_clock.GetUtcNow(), options.TimeZone);
        var envelope = new RecurringEnvelope<TCommand>(command, cron, uniqueKey, options.TimeZone, options.Overlap, nextDue) {
            TenantId = _tenant?.Current,
        };
        var entryKey = RecurringKeys.Entry(uniqueKey, nextDue);
        var inserted = await _store.TryStageAsync(entryKey, envelope, cancellationToken).ConfigureAwait(false);

        if(inserted) {
            await _dispatcher.EnqueueAsync(entryKey, envelope, cancellationToken).ConfigureAwait(false);
        }

        return uniqueKey;
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(string uniqueKey, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(uniqueKey);

        var prefix = RecurringKeys.Prefix(uniqueKey);
        var pending = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        var removed = false;

        foreach(var (key, _) in pending) {
            if(!key.StartsWith(prefix, StringComparison.Ordinal)) {
                continue;
            }

            await _store.CompleteAsync(key, cancellationToken).ConfigureAwait(false);
            removed = true;
        }

        return removed;
    }
}
