//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Interfaces;

/// <summary>Marker for a domain event or notification that can be published to zero or more consumers.</summary>
public interface IEvent;

/// <summary>
/// Marker for a notification (event) that is published to every registered notification handler.
/// This package carries the message contract only; handler interfaces live in the core package.
/// </summary>
public interface INotification : IEvent;
