//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Recurring;

/// <summary>
/// A deferred envelope that also knows how to schedule its next occurrence. Implemented by
/// <see cref="RecurringEnvelope{TCommand}"/> and persisted by the EF deferred store through the
/// recurrence columns.
/// </summary>
public interface IRecurringEnvelope : IDeferredEnvelope {
    /// <summary>Gets the stable schedule key.</summary>
    string UniqueKey { get; }

    /// <summary>Gets the original cron expression text.</summary>
    string Expression { get; }

    /// <summary>Gets the cron expression.</summary>
    CronExpression Cron { get; }

    /// <summary>Gets the time zone in which the cron fields are evaluated.</summary>
    TimeZoneInfo TimeZone { get; }

    /// <summary>Gets the overlap policy.</summary>
    RecurringOverlapPolicy Overlap { get; }

    /// <summary>Creates the next occurrence at <paramref name="dueUtc"/> with the same settings.</summary>
    /// <param name="dueUtc">The next due time.</param>
    IDeferredEnvelope CreateNext(DateTimeOffset dueUtc);
}
