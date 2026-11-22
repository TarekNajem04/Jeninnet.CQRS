//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Notifications;

/// <summary>
/// Options controlling notification publishing.
/// </summary>
public sealed class NotificationPublishOptions {
    /// <summary>
    /// Gets or sets the delivery strategy. Defaults to <see cref="NotificationPublishStrategy.ForEachAwait"/>.
    /// </summary>
    public NotificationPublishStrategy Strategy { get; set; } = NotificationPublishStrategy.ForEachAwait;
}
