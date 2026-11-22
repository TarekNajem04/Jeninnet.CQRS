//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Shared send/publish lifecycle for every transport. The transport supplies the broker-specific
/// operation; this wrapper owns the observer notifications and cancellation handling so the
/// behavior is identical across brokers.
/// </summary>
internal static class TransportOperation {
    /// <summary>
    /// Sends one envelope and reports the send lifecycle to the registered observers.
    /// </summary>
    /// <param name="observers">The optional observer invoker.</param>
    /// <param name="message">The original message.</param>
    /// <param name="messageType">The runtime message type.</param>
    /// <param name="envelope">The prepared transport envelope.</param>
    /// <param name="send">The broker-specific send operation.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    public static async Task SendAsync(
        Jeninnet.CQRS.Observers.MessageObserverInvoker? observers,
        object message,
        Type messageType,
        TransportEnvelope envelope,
        Func<CancellationToken, Task> send,
        CancellationToken cancellationToken) {
        var observation = observers is { HasSendObservers: true }
            ? new Jeninnet.CQRS.Observers.OutboundObservationContext(message, messageType, envelope)
            : null;

        if(observation is not null) {
            await observers!.OnSendingAsync(observation, cancellationToken).ConfigureAwait(false);
        }

        try {
            await send(cancellationToken).ConfigureAwait(false);

            if(observation is not null) {
                await observers!.OnSentAsync(observation, cancellationToken).ConfigureAwait(false);
            }
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch(Exception error) {
            if(observation is not null) {
                await observers!.OnSendFaultAsync(observation, error, CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
    }

    /// <summary>
    /// Publishes one envelope and reports the publish lifecycle to the registered observers.
    /// </summary>
    /// <param name="observers">The optional observer invoker.</param>
    /// <param name="message">The original message.</param>
    /// <param name="messageType">The runtime message type.</param>
    /// <param name="envelope">The prepared transport envelope.</param>
    /// <param name="publish">The broker-specific publish operation.</param>
    /// <param name="cancellationToken">Cancels the publish.</param>
    public static async Task PublishAsync(
        Jeninnet.CQRS.Observers.MessageObserverInvoker? observers,
        object message,
        Type messageType,
        TransportEnvelope envelope,
        Func<CancellationToken, Task> publish,
        CancellationToken cancellationToken) {
        var observation = observers is { HasPublishObservers: true }
            ? new Jeninnet.CQRS.Observers.OutboundObservationContext(message, messageType, envelope)
            : null;

        if(observation is not null) {
            await observers!.OnPublishingAsync(observation, cancellationToken).ConfigureAwait(false);
        }

        try {
            await publish(cancellationToken).ConfigureAwait(false);

            if(observation is not null) {
                await observers!.OnPublishedAsync(observation, cancellationToken).ConfigureAwait(false);
            }
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch(Exception error) {
            if(observation is not null) {
                await observers!.OnPublishFaultAsync(observation, error, CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
    }
}