//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Notifications;

/// <summary>
/// Publishes notifications immediately, bypassing any outbox decorator. The outbox replay
/// dispatcher resolves this contract so replayed notifications do not get staged again.
/// </summary>
public interface IDirectNotificationPublisher : INotificationPublisher;
