//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Outbox;

/// <summary>
/// Options for the durable notification outbox dispatcher.
/// </summary>
public sealed class NotificationOutboxOptions {
    /// <summary>
    /// Gets or sets how often the dispatcher retries pending notifications. Defaults to five
    /// seconds; non-positive values fall back to the default.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
}