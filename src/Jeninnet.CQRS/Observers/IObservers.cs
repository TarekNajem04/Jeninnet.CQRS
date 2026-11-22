//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Observers;

/// <summary>
/// Observes every point-to-point send accepted by a transport. Observers run in registration
/// order; an observer failure is logged and counted but never fails the send.
/// </summary>
public interface ISendObserver {
    /// <summary>Runs before the transport sends the envelope.</summary>
    /// <param name="context">The message and canonical envelope.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    Task OnSendingAsync(OutboundObservationContext context, CancellationToken cancellationToken = default);

    /// <summary>Runs after the transport accepts the send.</summary>
    /// <param name="context">The message and canonical envelope.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    Task OnSentAsync(OutboundObservationContext context, CancellationToken cancellationToken = default);

    /// <summary>Runs when the send fails.</summary>
    /// <param name="context">The message and canonical envelope.</param>
    /// <param name="error">The failure.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    Task OnSendFaultAsync(OutboundObservationContext context, Exception error, CancellationToken cancellationToken = default);
}

/// <summary>
/// Observes every publish accepted by a transport. Observers run in registration order; an
/// observer failure is logged and counted but never fails the publish.
/// </summary>
public interface IPublishObserver {
    /// <summary>Runs before the transport publishes the envelope.</summary>
    /// <param name="context">The message and canonical envelope.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    Task OnPublishingAsync(OutboundObservationContext context, CancellationToken cancellationToken = default);

    /// <summary>Runs after the transport accepts the publish.</summary>
    /// <param name="context">The message and canonical envelope.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    Task OnPublishedAsync(OutboundObservationContext context, CancellationToken cancellationToken = default);

    /// <summary>Runs when the publish fails.</summary>
    /// <param name="context">The message and canonical envelope.</param>
    /// <param name="error">The failure.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    Task OnPublishFaultAsync(OutboundObservationContext context, Exception error, CancellationToken cancellationToken = default);
}

/// <summary>
/// Observes the consume lifecycle for every inbound envelope. Observers run in registration order;
/// an observer failure is logged and counted but never fails the delivery.
/// </summary>
public interface IConsumeObserver {
    /// <summary>Runs before filters and the consumer handler.</summary>
    /// <param name="context">The delivered envelope and endpoint.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    Task OnConsumingAsync(ConsumeObservationContext context, CancellationToken cancellationToken = default);

    /// <summary>Runs after the consumer handler succeeds.</summary>
    /// <param name="context">The delivered envelope and endpoint.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    Task OnConsumedAsync(ConsumeObservationContext context, CancellationToken cancellationToken = default);

    /// <summary>Runs when filtering or handling fails.</summary>
    /// <param name="context">The delivered envelope and endpoint.</param>
    /// <param name="error">The failure.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    Task OnConsumeFaultAsync(ConsumeObservationContext context, Exception error, CancellationToken cancellationToken = default);
}
