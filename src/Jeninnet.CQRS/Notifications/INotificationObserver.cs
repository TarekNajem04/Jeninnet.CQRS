//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Notifications;

/// <summary>
/// A process-wide observer of every published notification. Unlike a typed
/// <see cref="Interfaces.INotificationHandler{TNotification}"/>, an observer sees the runtime
/// message and its type without knowing every contract at compile time; sagas use this seam to
/// route events into their transition tables without reflection.
/// </summary>
/// <remarks>
/// Observers run after typed handlers. An observer failure propagates to the publisher, so a
/// consumer can retry the original operation. Observers must be thread-safe: the publisher is a
/// singleton and may run concurrently.
/// </remarks>
public interface INotificationObserver {
    /// <summary>Observes one published notification.</summary>
    /// <param name="notification">The notification instance.</param>
    /// <param name="notificationType">The runtime notification type.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    Task OnPublishingAsync(object notification, Type notificationType, CancellationToken cancellationToken = default);
}