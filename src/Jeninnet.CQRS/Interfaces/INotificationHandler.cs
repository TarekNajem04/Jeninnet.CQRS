//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Interfaces;

/// <summary>
/// Handles a notification of type <typeparamref name="TNotification"/>.
/// </summary>
/// <typeparam name="TNotification">The notification type. Contravariant so one handler can serve several notification types.</typeparam>
public interface INotificationHandler<in TNotification> where TNotification : INotification {
    /// <summary>
    /// Handles the notification.
    /// </summary>
    /// <param name="notification">The published notification.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    Task HandleAsync(TNotification notification, CancellationToken cancellationToken);
}