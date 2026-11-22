//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Test/development <see cref="IMessageTransport"/>: serializes each message into the canonical
/// envelope and delivers it directly to the registered handlers. Send reaches one handler;
/// publish reaches all of them. No broker, no network, no background threads.
/// </summary>
/// <remarks>
/// With no <see cref="IMessageTransportHandler"/> registered, messages are accepted and dropped,
/// matching a broker with no consumers. Start the transport before publishing; stops make later
/// sends fail instead of silently losing messages.
/// </remarks>
/// <param name="serializer">Serializes payloads and supplies the content type.</param>
/// <param name="handlers">Consumer-side handlers; an empty sequence means no local consumers.</param>
/// <param name="namingConvention">Resolves exchange/queue/topic names.</param>
/// <param name="clock">Stamps envelopes; defaults to <see cref="TimeProvider.System"/>.</param>
/// <param name="tenant">The ambient tenant adapter; <see langword="null"/> without tenancy.</param>
/// <param name="receivePipeline">Applies endpoint filters, retry, and consumption timeout; <see langword="null"/> invokes handlers directly.</param>
/// <param name="observers">Send, publish, and consume observers; <see langword="null"/> when observation is not configured.</param>
public sealed class InMemoryTransport(
    IMessageSerializer serializer,
    IEnumerable<IMessageTransportHandler> handlers,
    TransportNamingConvention namingConvention,
    TimeProvider? clock = null,
    TenantAccessor? tenant = null,
    ReceiveFilterPipeline? receivePipeline = null,
    MessageObserverInvoker? observers = null) : IMessageTransport {
    private readonly IMessageTransportHandler[] _handlers = handlers?.ToArray() ?? [];
    private readonly ReceiveFilterPipeline? _receivePipeline = receivePipeline;
    private readonly MessageObserverInvoker? _observers = observers;
    private readonly Lock _sync = new();
    private readonly TransportEnvelopeBuilder _envelopes = TransportEnvelopeBuilders.ForTenantAware(
        serializer,
        (messageType, _) => namingConvention.Describe(messageType),
        () => tenant?.Current,
        () => (clock ?? TimeProvider.System).GetUtcNow());
    private bool _started;

    /// <inheritdoc />
    public async Task SendAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) {
        Guard.NotNull(message);
        Guard.NotNull(messageType);
        EnsureStarted();

        var envelope = CreateEnvelope(message, messageType, options, TransportDeliveryMode.Send);

        await TransportOperation.SendAsync(
            _observers,
            message,
            messageType,
            envelope,
            ct => _handlers.Length > 0
                ? HandleAsync(_handlers[0], envelope, ct)
                : Task.CompletedTask,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PublishAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) {
        Guard.NotNull(message);
        Guard.NotNull(messageType);
        EnsureStarted();

        var envelope = CreateEnvelope(message, messageType, options, TransportDeliveryMode.Publish);

        await TransportOperation.PublishAsync(
            _observers,
            message,
            messageType,
            envelope,
            async ct => {
                foreach(var handler in _handlers) {
                    await HandleAsync(handler, envelope, ct).ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default) {
        lock(_sync) {
            _started = true;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default) {
        lock(_sync) {
            _started = false;
        }

        return Task.CompletedTask;
    }

    private async Task HandleAsync(IMessageTransportHandler handler, TransportEnvelope envelope, CancellationToken cancellationToken) {
        if(_receivePipeline is null) {
            await handler.HandleAsync(envelope, cancellationToken).ConfigureAwait(false);

            return;
        }

        await _receivePipeline.HandleAsync(handler, envelope, cancellationToken).ConfigureAwait(false);
    }

    private TransportEnvelope CreateEnvelope(object message, Type messageType, TransportMessageOptions? options, TransportDeliveryMode deliveryMode) =>
        _envelopes.Create(message, messageType, options, deliveryMode);

    private void EnsureStarted() {
        lock(_sync) {
            if(!_started) {
                throw new InvalidOperationException("The in-memory transport is not started. Call StartAsync before sending or publishing.");
            }
        }
    }
}