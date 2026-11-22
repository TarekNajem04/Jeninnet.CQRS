//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Notifications;

/// <summary>
/// Publishes a notification to all registered handlers.
/// </summary>
public interface INotificationPublisher {
    /// <summary>
    /// Publishes <paramref name="notification"/> to every registered
    /// <see cref="Interfaces.INotificationHandler{TNotification}"/>.
    /// Zero handlers is a no-op.
    /// </summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <param name="notification">The notification to publish.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    Task PublishAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : Interfaces.INotification;
}