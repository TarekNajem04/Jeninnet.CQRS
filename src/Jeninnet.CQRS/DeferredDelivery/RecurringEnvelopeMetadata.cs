//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.DeferredDelivery;

/// <summary>
/// Persisted recurrence settings for a deferred row. The store keeps these columns beside the
/// payload so a restarted host can rebuild a <see cref="Recurring.RecurringEnvelope{TCommand}"/>
/// without reflection.
/// </summary>
/// <param name="UniqueKey">The stable schedule key.</param>
/// <param name="CronExpression">The five-field cron expression text.</param>
/// <param name="TimeZoneId">The time zone in which the cron fields are evaluated.</param>
/// <param name="Overlap">What to do when the previous occurrence is still running.</param>
public sealed record RecurringEnvelopeMetadata(string UniqueKey, string CronExpression, string TimeZoneId, RecurringOverlapPolicy Overlap);