//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Routes sends and publishes to the rider selected by the message-type table. The router is the
/// primary <see cref="IMessageTransport"/> when riders are registered; it never starts or stops a
/// rider itself. <see cref="CqrsRiderHost"/> owns the deterministic lifecycle.
/// </summary>
public sealed class CqrsRiderTransport(CqrsRiderRegistry registry) : IMessageTransport {
    private readonly CqrsRiderRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    /// <summary>Gets the routing table used by this router.</summary>
    public CqrsRiderRegistry Registry => _registry;

    /// <inheritdoc />
    public Task SendAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) {
        Guard.NotNull(message);
        Guard.NotNull(messageType);

        return _registry.Resolve(messageType).Transport.SendAsync(message, messageType, options, cancellationToken);
    }

    /// <inheritdoc />
    public Task PublishAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) {
        Guard.NotNull(message);
        Guard.NotNull(messageType);

        return _registry.Resolve(messageType).Transport.PublishAsync(message, messageType, options, cancellationToken);
    }

    /// <summary>
    /// No-op: the rider host starts every rider in registration order. A router that started
    /// individual transports would break the ownership and shutdown guarantees.
    /// </summary>
    /// <param name="cancellationToken">Ignored.</param>
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>No-op: <see cref="CqrsRiderHost"/> stops riders in reverse start order.</summary>
    /// <param name="cancellationToken">Ignored.</param>
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
