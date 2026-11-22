//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.SignalR;

/// <summary>
/// Client-side <see cref="IMessageTransport"/> over a SignalR hub. Inbound deliveries rebuild the
/// canonical envelope and run the registered consumer handlers; outbound sends invoke the hub,
/// which routes them back to the caller (send) or to every connected client (publish).
/// </summary>
/// <remarks>
/// The transport owns one <see cref="HubConnection"/> and reconnects it automatically. SignalR is
/// ephemeral: messages sent while a client is disconnected are not replayed, and there is no
/// broker-side acknowledgement. Use an outbox/relay for durable delivery. For scale-out, configure
/// the hub with a SignalR backplane (for example Redis); the transport itself is stateless.
/// </remarks>
public sealed class SignalRTransport : IMessageTransport, IAsyncDisposable, IDisposable {
    private static readonly Action<ILogger, string?, string?, Exception?> _deliveryFailed =
        LoggerMessage.Define<string?, string?>(
            LogLevel.Warning,
            new EventId(1, "SignalRDeliveryFault"),
            "SignalR delivery to {Handler} failed for message {MessageId}.");
    private static readonly Action<ILogger, Exception?> _reconnecting =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(2, "SignalRReconnecting"),
            "The SignalR transport lost its connection and is reconnecting.");
    private static readonly Action<ILogger, string?, Exception?> _reconnected =
        LoggerMessage.Define<string?>(
            LogLevel.Information,
            new EventId(3, "SignalRReconnected"),
            "The SignalR transport reconnected with connection id {ConnectionId}.");
    private static readonly Action<ILogger, Exception?> _closed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(4, "SignalRClosed"),
            "The SignalR transport closed its connection.");

    private readonly SignalRTransportOptions _options;
    private readonly IMessageTransportHandler[] _handlers;
    private readonly TransportNamingConvention _namingConvention;
    private readonly TimeProvider _clock;
    private readonly TenantAccessor? _tenant;
    private readonly ReceiveFilterPipeline? _receivePipeline;
    private readonly MessageObserverInvoker? _observers;
    private readonly ILogger<SignalRTransport>? _logger;
    private readonly TransportEnvelopeBuilder _envelopes;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _sync = new();

    private HubConnection? _connection;
    private bool _started;
    private bool _disposed;

    /// <summary>Initializes the transport.</summary>
    /// <param name="options">Hub URL, reconnection, and connection customization options.</param>
    /// <param name="serializer">Serializes payloads and supplies the content type.</param>
    /// <param name="handlers">Consumer-side handlers; publish reaches all, send reaches the first.</param>
    /// <param name="namingConvention">Resolves exchange/queue/topic names.</param>
    /// <param name="clock">Stamps envelopes; defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="tenant">The ambient tenant adapter; <see langword="null"/> without tenancy.</param>
    /// <param name="receivePipeline">Applies endpoint filters, retry, and consumption timeout.</param>
    /// <param name="observers">Send, publish, and consume observers; <see langword="null"/> when not configured.</param>
    /// <param name="logger">Optional diagnostic logger for reconnects and handler failures.</param>
    public SignalRTransport( // NOSONAR: Public options/records intentionally group related settings; splitting would fragment the API.
        IOptions<SignalRTransportOptions> options,
        IMessageSerializer serializer,
        IEnumerable<IMessageTransportHandler> handlers,
        TransportNamingConvention namingConvention,
        TimeProvider? clock = null,
        TenantAccessor? tenant = null,
        ReceiveFilterPipeline? receivePipeline = null,
        MessageObserverInvoker? observers = null,
        ILogger<SignalRTransport>? logger = null) {
        _options = Validate((options ?? throw new ArgumentNullException(nameof(options))).Value);
        var payloadSerializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _handlers = handlers?.ToArray() ?? [];
        _namingConvention = namingConvention ?? throw new ArgumentNullException(nameof(namingConvention));
        _clock = clock ?? TimeProvider.System;
        _tenant = tenant;
        _receivePipeline = receivePipeline;
        _observers = observers;
        _logger = logger;
        _envelopes = TransportEnvelopeBuilders.ForTenantAware(
            payloadSerializer,
            (messageType, _) => _namingConvention.Describe(messageType),
            () => _tenant?.Current,
            () => _clock.GetUtcNow());
    }

    /// <summary>Gets the current connection state; <see cref="HubConnectionState.Disconnected"/> before start.</summary>
    public HubConnectionState ConnectionState {
        get {
            lock(_sync) {
                return _connection?.State ?? HubConnectionState.Disconnected;
            }
        }
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default) {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try {
            HubConnection connection;

            lock(_sync) {
                Guard.NotDisposed(_disposed, this);

                if(_started) {
                    return;
                }

                connection = _connection ??= BuildConnection();
                _started = true;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.StartTimeout);

            try {
                await connection.StartAsync(timeout.Token).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested) {
                ResetStarted();

                throw new TimeoutException($"The SignalR transport did not connect within {_options.StartTimeout}.");
            }
            catch {
                ResetStarted();

                throw;
            }
        }
        finally {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default) {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try {
            HubConnection? connection;

            lock(_sync) {
                if(!_started && _connection is null) {
                    return;
                }

                _started = false;
                connection = _connection;
            }

            if(connection is not null && connection.State is not HubConnectionState.Disconnected) {
                await connection.StopAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SendAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) {
        Guard.NotNull(message);
        Guard.NotNull(messageType);

        var envelope = CreateEnvelope(message, messageType, options, TransportDeliveryMode.Send);

        await TransportOperation.SendAsync(
            _observers,
            message,
            messageType,
            envelope,
            ct => InvokeAsync(SignalRTransportProtocol.SendMethod, envelope, ct),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PublishAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) {
        Guard.NotNull(message);
        Guard.NotNull(messageType);

        var envelope = CreateEnvelope(message, messageType, options, TransportDeliveryMode.Publish);

        await TransportOperation.PublishAsync(
            _observers,
            message,
            messageType,
            envelope,
            ct => InvokeAsync(SignalRTransportProtocol.PublishMethod, envelope, ct),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() {
        await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        try {
            HubConnection? connection;

            lock(_sync) {
                if(_disposed) {
                    return;
                }

                _disposed = true;
                _started = false;
                connection = _connection;
                _connection = null;
            }

            if(connection is not null) {
                try {
                    if(connection.State is not HubConnectionState.Disconnected) {
                        await connection.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch {
                    // Closing a connection is best-effort; disposal still releases the client.
                }

                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally {
            _lifecycleGate.Release();
            _lifecycleGate.Dispose();
        }
    }

    /// <inheritdoc />
    public void Dispose() =>
        DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task InvokeAsync(string method, TransportEnvelope envelope, CancellationToken cancellationToken) {
        HubConnection connection;

        lock(_sync) {
            Guard.NotDisposed(_disposed, this);

            if(!_started || _connection is null) {
                throw new InvalidOperationException("The SignalR transport is not started. Call StartAsync before sending or publishing.");
            }

            connection = _connection;
        }

        if(connection.State is not HubConnectionState.Connected) {
            throw new InvalidOperationException($"The SignalR transport is not connected (state: {connection.State}). The outbox relay can retry the message.");
        }

        await connection.InvokeAsync(method, ToWire(envelope), cancellationToken).ConfigureAwait(false);
    }

    private HubConnection BuildConnection() {
        var builder = new HubConnectionBuilder()
            .WithUrl(_options.HubUrl, connectionOptions => _options.ConfigureConnection?.Invoke(connectionOptions));

        if(_options.AutomaticReconnect) {
            builder.WithAutomaticReconnect(new SignalRRetryPolicy([.. _options.ReconnectDelays]));
        }

        var connection = builder.Build();
        connection.On<SignalRWireEnvelope>(SignalRTransportProtocol.DeliveryMethod, OnDeliveryAsync);
        connection.Reconnecting += OnReconnectingAsync;
        connection.Reconnected += OnReconnectedAsync;
        connection.Closed += OnClosedAsync;

        return connection;
    }

    private Task OnDeliveryAsync(SignalRWireEnvelope? wire) {
        if(wire is null) {
            return Task.CompletedTask;
        }

        return DispatchAsync(FromWire(wire));
    }

    private async Task DispatchAsync(TransportEnvelope envelope) {
        if(_handlers.Length is 0) {
            return;
        }

        if(envelope.DeliveryMode is TransportDeliveryMode.Send) {
            await DeliverAsync(_handlers[0], envelope).ConfigureAwait(false);

            return;
        }

        foreach(var handler in _handlers) {
            await DeliverAsync(handler, envelope).ConfigureAwait(false);
        }
    }

    private async Task DeliverAsync(IMessageTransportHandler handler, TransportEnvelope envelope) {
        try {
            if(_receivePipeline is null) {
                await handler.HandleAsync(envelope, CancellationToken.None).ConfigureAwait(false);
            } else {
                await _receivePipeline.HandleAsync(handler, envelope, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch(Exception error) {
            if(_logger is not null) {
                _deliveryFailed(_logger, handler.GetType().FullName, envelope.MessageId, error);
            }
        }
    }

    private Task OnReconnectingAsync(Exception? error) {
        if(_logger is not null) {
            _reconnecting(_logger, error);
        }

        return Task.CompletedTask;
    }

    private Task OnReconnectedAsync(string? connectionId) {
        if(_logger is not null) {
            _reconnected(_logger, connectionId, null);
        }

        return Task.CompletedTask;
    }

    private Task OnClosedAsync(Exception? error) {
        if(error is not null && _logger is not null) {
            _closed(_logger, error);
        }

        return Task.CompletedTask;
    }

    private void ResetStarted() {
        lock(_sync) {
            _started = false;
        }
    }

    private TransportEnvelope CreateEnvelope(object message, Type messageType, TransportMessageOptions? options, TransportDeliveryMode deliveryMode) =>
        _envelopes.Create(message, messageType, options, deliveryMode);

    private static SignalRWireEnvelope ToWire(TransportEnvelope envelope) =>
        new(
            envelope.MessageId,
            envelope.MessageType,
            envelope.ContentType,
            envelope.Payload.ToArray(),
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.TenantId,
            envelope.PartitionKey,
            envelope.Timestamp,
            new Dictionary<string, string>(envelope.Headers, StringComparer.Ordinal),
            envelope.Topology.Exchange,
            envelope.Topology.Queue,
            envelope.Topology.Topic,
            envelope.Topology.RoutingKey,
            envelope.DeliveryMode is TransportDeliveryMode.Send ? "send" : "publish",
            envelope.MessageVersion);

    private static TransportEnvelope FromWire(SignalRWireEnvelope wire) {
        var headers = wire.Headers is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(wire.Headers, StringComparer.Ordinal);
        var messageVersion = Math.Max(1, wire.MessageVersion);
        var contentType = StringCompat.IsNullOrWhiteSpace(wire.ContentType) ? "application/octet-stream" : wire.ContentType;

        if(!headers.ContainsKey(TransportHeaders.MessageType)) {
            TransportHeaders.SetMetadata(headers, wire.MessageType, messageVersion, contentType);
        }

        return new TransportEnvelope(
            StringCompat.IsNullOrWhiteSpace(wire.MessageId) ? Guid.NewGuid().ToString("N") : wire.MessageId,
            wire.MessageType,
            contentType,
            wire.Payload ?? Array.Empty<byte>(),
            wire.CorrelationId,
            wire.CausationId,
            wire.TenantId,
            wire.PartitionKey,
            wire.Timestamp,
            headers,
            new TransportTopology(wire.Exchange, wire.Queue, wire.Topic, wire.RoutingKey),
            string.Equals(wire.DeliveryMode, "send", StringComparison.OrdinalIgnoreCase)
                ? TransportDeliveryMode.Send
                : TransportDeliveryMode.Publish) {
            MessageVersion = messageVersion,
        };
    }

    private static SignalRTransportOptions Validate(SignalRTransportOptions options) {
        Guard.NotNull(options);

        if(StringCompat.IsNullOrWhiteSpace(options.HubUrl)) {
            throw new ArgumentException("HubUrl must not be empty.", nameof(options));
        }

        if(options.StartTimeout <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options), options.StartTimeout, "StartTimeout must be greater than zero.");
        }

        if(options.AutomaticReconnect && options.ReconnectDelays.Count is 0) {
            throw new ArgumentException("At least one reconnect delay is required when automatic reconnect is enabled.", nameof(options));
        }

        foreach(var delay in options.ReconnectDelays) {
            if(delay < TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(options), delay, "Reconnect delays must not be negative.");
            }
        }

        return options;
    }
}
