//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Recurring;

/// <summary>What to do when a due occurrence finds the same schedule already running.</summary>
public enum RecurringOverlapPolicy {
    /// <summary>Skip the busy occurrence (default); the next occurrence is still scheduled.</summary>
    Skip = 0,

    /// <summary>Wait for the running occurrence, then run this one.</summary>
    Queue = 1,
}

/// <summary>
/// Per-schedule options for <see cref="IRecurringDispatcher.ScheduleAsync{TCommand}"/>.
/// </summary>
public sealed class RecurringScheduleOptions {
    /// <summary>
    /// Gets or sets the stable key that deduplicates schedules across calls and instances.
    /// Defaults to the command type's full name.
    /// </summary>
    public string? UniqueKey { get; set; }

    /// <summary>
    /// Gets or sets the time zone in which the cron fields are evaluated. Defaults to UTC.
    /// </summary>
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Utc;

    /// <summary>
    /// Gets or sets the overlap policy. Defaults to <see cref="RecurringOverlapPolicy.Skip"/>.
    /// </summary>
    public RecurringOverlapPolicy Overlap { get; set; } = RecurringOverlapPolicy.Skip;
}
