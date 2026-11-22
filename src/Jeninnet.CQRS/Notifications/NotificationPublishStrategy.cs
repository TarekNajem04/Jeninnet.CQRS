//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Notifications;

/// <summary>
/// How <see cref="INotificationPublisher"/> delivers a notification to its handlers.
/// </summary>
public enum NotificationPublishStrategy {
    /// <summary>
    /// Handlers run one after another in registration order. The first exception propagates
    /// immediately and later handlers do not run (fail-fast delivery).
    /// </summary>
    ForEachAwait = 0,

    /// <summary>
    /// All handlers start together; the first exception propagates after every handler
    /// has been started (Task.WhenAll semantics).
    /// </summary>
    Parallel = 1
}
