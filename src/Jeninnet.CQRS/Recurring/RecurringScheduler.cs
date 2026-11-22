//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Recurring;

/// <summary>Deterministic deferred-store keys for recurring occurrences.</summary>
internal static class RecurringKeys {
    internal static string Entry(string uniqueKey, DateTimeOffset dueUtc) =>
        $"recurring:{uniqueKey}:{dueUtc.UtcTicks}";

    internal static string Prefix(string uniqueKey) =>
        $"recurring:{uniqueKey}:";
}

/// <summary>Stages the next occurrence of a recurring envelope.</summary>
internal interface IRecurringScheduler {
    Task StageNextAsync(IRecurringEnvelope current, DateTimeOffset afterUtc, CancellationToken cancellationToken);
}

/// <summary>
/// Computes and stages the next occurrence on the durable deferred store. The deterministic key
/// makes concurrent stagers (or a replay after a crash) converge on one entry.
/// </summary>
internal sealed class RecurringScheduler(IKeyedDeferredStore store, IDeferredDispatcher dispatcher) : IRecurringScheduler {
    private readonly IKeyedDeferredStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IDeferredDispatcher _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    public async Task StageNextAsync(IRecurringEnvelope current, DateTimeOffset afterUtc, CancellationToken cancellationToken) {
        Guard.NotNull(current);

        // Never schedule at or before the current occurrence, even if the clock runs early.
        var after = afterUtc > current.DueUtc ? afterUtc : current.DueUtc;
        var nextDue = current.Cron.GetNextOccurrence(after, current.TimeZone);
        var next = current.CreateNext(nextDue);
        var key = RecurringKeys.Entry(current.UniqueKey, nextDue);
        var inserted = await _store.TryStageAsync(key, next, cancellationToken).ConfigureAwait(false);

        if(inserted) {
            // The pump owns an in-memory queue; staging alone is only visible after a restart.
            await _dispatcher.EnqueueAsync(key, next, cancellationToken).ConfigureAwait(false);
        }
    }
}
