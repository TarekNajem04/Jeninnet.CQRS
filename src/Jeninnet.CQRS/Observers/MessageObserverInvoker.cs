//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Observers;

/// <summary>
/// One invocation helper for every lifecycle phase. It caches the observer arrays, exposes
/// zero-work fast paths for the no-observer case, and isolates every observer exception so a
/// logging or tracing hook can never break messaging.
/// </summary>
/// <remarks>
/// Transports and the receive pipeline call this helper, never the observers directly. The
/// no-observer path returns <see cref="Task.CompletedTask"/> without allocating a context or a
/// state machine; callers should check the matching <c>Has...Observers</c> property before
/// constructing an observation context.
/// </remarks>
/// <param name="send">Registered send observers in registration order.</param>
/// <param name="publish">Registered publish observers in registration order.</param>
/// <param name="consume">Registered consume observers in registration order.</param>
/// <param name="metrics">Optional fault counter.</param>
/// <param name="logger">Optional logger for isolated observer exceptions.</param>
public sealed class MessageObserverInvoker(
    IEnumerable<ISendObserver>? send = null,
    IEnumerable<IPublishObserver>? publish = null,
    IEnumerable<IConsumeObserver>? consume = null,
    MessageObserverMetrics? metrics = null,
    ILogger<MessageObserverInvoker>? logger = null) {
    private static readonly Action<ILogger, string, string, Exception?> _observerFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(1, "MessageObserverFault"),
            "Message observer '{Observer}' failed during {Operation}; the operation continues.");

    private readonly ISendObserver[] _send = send?.ToArray() ?? [];
    private readonly IPublishObserver[] _publish = publish?.ToArray() ?? [];
    private readonly IConsumeObserver[] _consume = consume?.ToArray() ?? [];
    private readonly MessageObserverMetrics? _metrics = metrics;
    private readonly ILogger<MessageObserverInvoker>? _logger = logger;

    /// <summary>Gets whether any send observer is registered.</summary>
    public bool HasSendObservers => _send.Length > 0;

    /// <summary>Gets whether any publish observer is registered.</summary>
    public bool HasPublishObservers => _publish.Length > 0;

    /// <summary>Gets whether any consume observer is registered.</summary>
    public bool HasConsumeObservers => _consume.Length > 0;

    /// <summary>Runs all send pre-observers.</summary>
    /// <param name="context">The message and envelope.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    public Task OnSendingAsync(OutboundObservationContext context, CancellationToken cancellationToken = default) {
        if(_send.Length is 0) {
            return Task.CompletedTask;
        }

        return SendPreAsync(context, cancellationToken);
    }

    /// <summary>Runs all send post-observers.</summary>
    /// <param name="context">The message and envelope.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    public Task OnSentAsync(OutboundObservationContext context, CancellationToken cancellationToken = default) {
        if(_send.Length is 0) {
            return Task.CompletedTask;
        }

        return SendPostAsync(context, cancellationToken);
    }

    /// <summary>Runs all send fault observers.</summary>
    /// <param name="context">The message and envelope.</param>
    /// <param name="error">The send failure.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    public Task OnSendFaultAsync(OutboundObservationContext context, Exception error, CancellationToken cancellationToken = default) {
        if(_send.Length is 0) {
            return Task.CompletedTask;
        }

        return SendFaultAsync(context, error, cancellationToken);
    }

    /// <summary>Runs all publish pre-observers.</summary>
    /// <param name="context">The message and envelope.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    public Task OnPublishingAsync(OutboundObservationContext context, CancellationToken cancellationToken = default) {
        if(_publish.Length is 0) {
            return Task.CompletedTask;
        }

        return PublishPreAsync(context, cancellationToken);
    }

    /// <summary>Runs all publish post-observers.</summary>
    /// <param name="context">The message and envelope.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    public Task OnPublishedAsync(OutboundObservationContext context, CancellationToken cancellationToken = default) {
        if(_publish.Length is 0) {
            return Task.CompletedTask;
        }

        return PublishPostAsync(context, cancellationToken);
    }

    /// <summary>Runs all publish fault observers.</summary>
    /// <param name="context">The message and envelope.</param>
    /// <param name="error">The publish failure.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    public Task OnPublishFaultAsync(OutboundObservationContext context, Exception error, CancellationToken cancellationToken = default) {
        if(_publish.Length is 0) {
            return Task.CompletedTask;
        }

        return PublishFaultAsync(context, error, cancellationToken);
    }

    /// <summary>Runs all consume pre-observers.</summary>
    /// <param name="context">The delivered envelope and endpoint.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    public Task OnConsumingAsync(ConsumeObservationContext context, CancellationToken cancellationToken = default) {
        if(_consume.Length is 0) {
            return Task.CompletedTask;
        }

        return ConsumePreAsync(context, cancellationToken);
    }

    /// <summary>Runs all consume post-observers.</summary>
    /// <param name="context">The delivered envelope and endpoint.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    public Task OnConsumedAsync(ConsumeObservationContext context, CancellationToken cancellationToken = default) {
        if(_consume.Length is 0) {
            return Task.CompletedTask;
        }

        return ConsumePostAsync(context, cancellationToken);
    }

    /// <summary>Runs all consume fault observers.</summary>
    /// <param name="context">The delivered envelope and endpoint.</param>
    /// <param name="error">The delivery failure.</param>
    /// <param name="cancellationToken">Cancels observation.</param>
    public Task OnConsumeFaultAsync(ConsumeObservationContext context, Exception error, CancellationToken cancellationToken = default) {
        if(_consume.Length is 0) {
            return Task.CompletedTask;
        }

        return ConsumeFaultAsync(context, error, cancellationToken);
    }

    private Task SendPreAsync(OutboundObservationContext context, CancellationToken cancellationToken) =>
        InvokeAsync(_send, observer => observer.OnSendingAsync(context, cancellationToken), "send", cancellationToken);

    private Task SendPostAsync(OutboundObservationContext context, CancellationToken cancellationToken) =>
        InvokeAsync(_send, observer => observer.OnSentAsync(context, cancellationToken), "sent", cancellationToken);

    private Task SendFaultAsync(OutboundObservationContext context, Exception error, CancellationToken cancellationToken) =>
        InvokeAsync(_send, observer => observer.OnSendFaultAsync(context, error, cancellationToken), "send-fault", cancellationToken);

    private Task PublishPreAsync(OutboundObservationContext context, CancellationToken cancellationToken) =>
        InvokeAsync(_publish, observer => observer.OnPublishingAsync(context, cancellationToken), "publish", cancellationToken);

    private Task PublishPostAsync(OutboundObservationContext context, CancellationToken cancellationToken) =>
        InvokeAsync(_publish, observer => observer.OnPublishedAsync(context, cancellationToken), "published", cancellationToken);

    private Task PublishFaultAsync(OutboundObservationContext context, Exception error, CancellationToken cancellationToken) =>
        InvokeAsync(_publish, observer => observer.OnPublishFaultAsync(context, error, cancellationToken), "publish-fault", cancellationToken);

    private Task ConsumePreAsync(ConsumeObservationContext context, CancellationToken cancellationToken) =>
        InvokeAsync(_consume, observer => observer.OnConsumingAsync(context, cancellationToken), "consume", cancellationToken);

    private Task ConsumePostAsync(ConsumeObservationContext context, CancellationToken cancellationToken) =>
        InvokeAsync(_consume, observer => observer.OnConsumedAsync(context, cancellationToken), "consumed", cancellationToken);

    private Task ConsumeFaultAsync(ConsumeObservationContext context, Exception error, CancellationToken cancellationToken) =>
        InvokeAsync(_consume, observer => observer.OnConsumeFaultAsync(context, error, cancellationToken), "consume-fault", cancellationToken);

    private async Task InvokeAsync<TObserver>(
        TObserver[] observers,
        Func<TObserver, Task> action,
        string operation,
        CancellationToken cancellationToken) where TObserver : class {
        foreach(var observer in observers) {
            try {
                await action(observer).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
                return;
            }
            catch(Exception error) {
                _metrics?.RecordFault(observer.GetType().Name, operation);

                if(_logger is not null) {
                    _observerFailed(_logger, observer.GetType().FullName ?? observer.GetType().Name, operation, error);
                }
            }
        }
    }
}
