//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Outbox;

/// <summary>
/// <see cref="INotificationPublisher"/> decorator that stages notifications in
/// <see cref="IOutboxStore"/> while a transaction is active and publishes them only after the
/// transaction commits.
/// </summary>
/// <remarks>
/// Outside a transaction the notification is staged durably and published immediately. Inside
/// a transaction the session queues it; a rollback discards the queue, so a notification from a
/// rolled-back handler is never published. At-least-once: if publishing fails or the process
/// dies after staging, the dispatcher retries, so handlers must be idempotent.
/// </remarks>
/// <param name="session">The scoped outbox session. Never <see langword="null"/>.</param>
/// <param name="clock">The clock used to stamp notifications. Never <see langword="null"/>.</param>
/// <param name="tenant">The ambient tenant adapter; <see langword="null"/> without tenancy.</param>
public sealed class OutboxNotificationPublisher(OutboxSession session, TimeProvider clock, TenantAccessor? tenant = null) : INotificationPublisher {
    private readonly OutboxSession _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly TenantAccessor? _tenant = tenant;

    /// <inheritdoc />
    public Task PublishAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification {
        Guard.NotNull(notification);

        return _session.StageAsync(new OutboxNotification<TNotification>(notification, _clock.GetUtcNow(), _tenant?.Current), cancellationToken);
    }
}