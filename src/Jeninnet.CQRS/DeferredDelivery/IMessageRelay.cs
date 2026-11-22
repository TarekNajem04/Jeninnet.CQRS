//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.DeferredDelivery;

/// <summary>
/// Executes a deferred envelope on behalf of the pump. The default re-dispatches
/// through the scoped broker; substitute it to observe, redirect, or bridge
/// deferred commands to a transport without touching the pump.
/// </summary>
public interface IMessageRelay {
    /// <summary>
    /// Executes <paramref name="envelope"/> with <paramref name="services"/>.
    /// </summary>
    Task RelayAsync(IDeferredEnvelope envelope, IServiceProvider services, CancellationToken cancellationToken = default);
}

/// <summary>
/// In-process <see cref="IMessageRelay"/>: dispatches through the scoped broker.
/// </summary>
public sealed class InProcessMessageRelay : IMessageRelay {
    /// <inheritdoc />
    public Task RelayAsync(IDeferredEnvelope envelope, IServiceProvider services, CancellationToken cancellationToken = default) {
        Guard.NotNull(envelope);
        Guard.NotNull(services);

        return envelope.DispatchAsync(services, cancellationToken);
    }
}
