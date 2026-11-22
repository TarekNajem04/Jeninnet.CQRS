//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Outbox;

/// <summary>
/// A notification staged in the outbox. Implements <see cref="IDeferredEnvelope"/> so the
/// outbox dispatcher can reuse the <see cref="IMessageRelay"/> seam; dispatch resolves the
/// direct publisher, never the outbox decorator (which would re-stage it forever).
/// </summary>
/// <typeparam name="TNotification">The notification type.</typeparam>
/// <param name="notification">The notification instance. Never <see langword="null"/>.</param>
/// <param name="stagedAtUtc">When the notification was accepted.</param>
/// <param name="tenantId">The tenant that published the notification, or <see langword="null"/>.</param>
public sealed class OutboxNotification<TNotification>(TNotification notification, DateTimeOffset stagedAtUtc, string? tenantId = null) : IDeferredEnvelope
    where TNotification : INotification {
    /// <summary>Gets the notification instance.</summary>
    public TNotification Notification { get; } = notification ?? throw new ArgumentNullException(nameof(notification));

    /// <inheritdoc />
    object IDeferredEnvelope.Command => Notification;

    /// <inheritdoc />
    public DateTimeOffset DueUtc { get; } = stagedAtUtc;

    /// <inheritdoc />
    public DateTimeOffset? ExpiresAtUtc => null;

    /// <inheritdoc />
    public int Version => 1;

    /// <inheritdoc />
    public string? TenantId { get; } = tenantId;

    /// <inheritdoc />
    public IDeferredEnvelope WithTenant(string? tenantId) =>
        new OutboxNotification<TNotification>(Notification, DueUtc, tenantId);

    /// <inheritdoc />
    public Task DispatchAsync(IServiceProvider services, CancellationToken cancellationToken) {
        Guard.NotNull(services);

        return services.GetRequiredService<IDirectNotificationPublisher>().PublishAsync(Notification, cancellationToken);
    }
}
