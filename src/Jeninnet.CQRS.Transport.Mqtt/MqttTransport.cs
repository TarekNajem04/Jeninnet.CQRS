//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Mqtt;

/// <summary>
/// MQTT <see cref="IMessageTransport"/>: topic-per-message-type with an optional tenant level,
/// configurable QoS, retained-message support, clean-session control, and an owned exponential
/// reconnect loop that resubscribes after a broker restart.
/// </summary>
/// <remarks>
/// MQTT has no broker-side point-to-point queue. Both send and publish travel on the message
/// topic; the delivery-mode user property tells this transport whether to dispatch to the first
/// local handler (send) or every local handler (publish). QoS 0/1/2 are broker-level guarantees
/// between the client and the broker; handler execution remains at-least-once because a client
/// can lose its session or crash after handling before the acknowledgement reaches the broker.
/// MQTT 5 user properties carry the envelope metadata; the topic is the routing key.
/// </remarks>
public sealed class MqttTransport : IMessageTransport, IAsyncDisposable, IDisposable {
    private static readonly Action<ILogger, MqttClientDisconnectReason, Exception?> _disconnected =
        LoggerMessage.Define<MqttClientDisconnectReason>(
            LogLevel.Warning,
            new EventId(1, "MqttDisconnected"),
            "The MQTT transport disconnected ({Reason}); reconnect is scheduled.");
    private static readonly Action<ILogger, Exception?> _reconnectFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(2, "MqttReconnectFailed"),
            "The MQTT transport reconnect attempt failed and will be retried.");
    private static readonly Action<ILogger, string, string, Exception?> _deliveryFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(3, "MqttDeliveryFault"),
            "MQTT delivery to {Handler} failed for message {MessageId}; the broker will redeliver it.");

    private readonly MqttTransportOptions _options;
    private readonly IMessageTransportHandler[] _handlers;
    private readonly MqttTopicConvention _topicConvention;
    private readonly ReceiveFilterPipeline? _receivePipeline;
    private readonly MessageObserverInvoker? _observers;
    private readonly TimeProvider _clock;
    private readonly TenantAccessor? _tenant;
    private readonly ILogger<MqttTransport>? _logger;
    private readonly MqttFactory _factory = new();
    private readonly MqttClientOptions _clientOptions;
    private readonly string _clientId;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly TransportEnvelopeBuilder _envelopes;

    private IMqttClient? _client;
    private CancellationTokenSource? _lifetime;
    private Task? _reconnectTask;
    private volatile bool _started;
    private volatile bool _stopping;
    private bool _disposed;
    private int _reconnectScheduled;

    /// <summary>Initializes the transport.</summary>
    /// <param name="options">Connection, topic, QoS, and reconnection options.</param>
    /// <param name="serializer">Serializes payloads and supplies the content type.</param>
    /// <param name="handlers">Consumer-side handlers; publish reaches all, send reaches the first.</param>
    /// <param name="topicConvention">The single topic formatter for message types and tenants.</param>
    /// <param name="clock">Stamps envelopes; defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="tenant">The ambient tenant adapter; <see langword="null"/> without tenancy.</param>
    /// <param name="receivePipeline">Applies endpoint filters, retry, and consumption timeout.</param>
    /// <param name="observers">Send, publish, and consume observers; <see langword="null"/> when not configured.</param>
    /// <param name="logger">Optional diagnostic logger for disconnects and handler failures.</param>
    public MqttTransport( // NOSONAR: Public options/records intentionally group related settings; splitting would fragment the API.
        IOptions<MqttTransportOptions> options,
        IMessageSerializer serializer,
        IEnumerable<IMessageTransportHandler> handlers,
        MqttTopicConvention topicConvention,
        TimeProvider? clock = null,
        TenantAccessor? tenant = null,
        ReceiveFilterPipeline? receivePipeline = null,
        MessageObserverInvoker? observers = null,
        ILogger<MqttTransport>? logger = null) {
        _options = Validate((options ?? throw new ArgumentNullException(nameof(options))).Value);
        var payloadSerializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _handlers = handlers?.ToArray() ?? [];
        _topicConvention = topicConvention ?? throw new ArgumentNullException(nameof(topicConvention));
        _clock = clock ?? TimeProvider.System;
        _tenant = tenant;
        _receivePipeline = receivePipeline;
        _observers = observers;
        _logger = logger;
        _envelopes = TransportEnvelopeBuilders.ForTenantAware(
            payloadSerializer,
            (messageType, tenantId) => new TransportTopology(
                _topicConvention.Prefix,
                string.Join(",", _topicConvention.GetSubscriptionFilters(messageType)),
                _topicConvention.Describe(messageType, tenantId),
                _topicConvention.GetMessageName(messageType)),
            () => _tenant?.Current,
            () => _clock.GetUtcNow());
        _clientId = StringCompat.IsNullOrWhiteSpace(_options.ClientId)
            ? $"jeninnet-cqrs-{Guid.NewGuid():N}"
            : _options.ClientId;
        _clientOptions = BuildClientOptions();
    }

    /// <summary>Gets whether the client currently has a broker connection.</summary>
    public bool IsConnected {
        get {
            lock(_sync) {
                return _client?.IsConnected is true;
            }
        }
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default) {
        CancellationTokenSource lifetime;

        lock(_sync) {
            Guard.NotDisposed(_disposed, this);

            if(_started) {
                return;
            }

            _started = true;
            _stopping = false;
            _lifetime = new CancellationTokenSource();
            lifetime = _lifetime;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);

        try {
            await ConnectAndSubscribeAsync(linked.Token).ConfigureAwait(false);
        }
        catch {
            lock(_sync) {
                _started = false;
                _stopping = true;
                _lifetime?.Dispose();
                _lifetime = null;
            }

            try {
                var failed = _client;

                if(failed is not null && failed.IsConnected) {
                    await failed.DisconnectAsync(
                        new MqttClientDisconnectOptionsBuilder()
                            .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                            .Build(),
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch {
                // Preserve the original connect/subscribe failure.
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default) {
        CancellationTokenSource? lifetime;
        Task? reconnect;

        lock(_sync) {
            _stopping = true;
            _started = false;
            lifetime = _lifetime;
            _lifetime = null;
            reconnect = _reconnectTask;
            _reconnectTask = null;
        }

        lifetime?.Cancel(); // NOSONAR: The synchronous API is intentional for netstandard2.0/net462 compatibility.

        var gateAcquired = false;

        try {
            await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            gateAcquired = true;
        }
        catch(OperationCanceledException) {
            // The shutdown budget is exhausted; disconnect without the gate below.
        }

        try {
            IMqttClient? client;

            lock(_sync) {
                client = _client;
            }

            if(client is not null && client.IsConnected) {
                try {
                    await client.DisconnectAsync(
                        new MqttClientDisconnectOptionsBuilder()
                            .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                            .Build(),
                        cancellationToken).ConfigureAwait(false);
                }
                catch {
                    // Disconnect is best-effort during shutdown.
                }
            }

            await TransportShutdown.WaitForReconnectAsync(reconnect).ConfigureAwait(false);
        }
        finally {
            TransportShutdown.Complete(lifetime, _connectGate, gateAcquired);
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
            ct => PublishCoreAsync(envelope, ct),
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
            ct => PublishCoreAsync(envelope, ct),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() {
        lock(_sync) {
            if(_disposed) {
                return;
            }
        }

        await StopAsync(CancellationToken.None).ConfigureAwait(false);

        IMqttClient? client;

        lock(_sync) {
            if(_disposed) {
                return;
            }

            _disposed = true;
            client = _client;
            _client = null;
        }

        if(client is not null) {
            client.ApplicationMessageReceivedAsync -= OnApplicationMessageReceivedAsync;
            client.DisconnectedAsync -= OnDisconnectedAsync;
            client.Dispose();
        }

        _connectGate.Dispose();
    }

    /// <inheritdoc />
    public void Dispose() =>
        DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task ConnectAndSubscribeAsync(CancellationToken cancellationToken) {
        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try {
            var client = EnsureClient();

            if(!client.IsConnected) {
                var result = await client.ConnectAsync(_clientOptions, cancellationToken).ConfigureAwait(false);

                if(result.ResultCode is not MqttClientConnectResultCode.Success) {
                    throw new InvalidOperationException(
                        $"The MQTT broker rejected the connection with {result.ResultCode}. {result.ReasonString}".Trim());
                }
            }

            if(_stopping) {
                throw new OperationCanceledException("The MQTT transport stopped while connecting.");
            }

            await SubscribeAsync(client, cancellationToken).ConfigureAwait(false);
        }
        finally {
            _connectGate.Release();
        }
    }

    private IMqttClient EnsureClient() {
        lock(_sync) {
            Guard.NotDisposed(_disposed, this);

            if(_client is null) {
                _client = _factory.CreateMqttClient();
                _client.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
                _client.DisconnectedAsync += OnDisconnectedAsync;
            }

            return _client;
        }
    }

    private async Task SubscribeAsync(IMqttClient client, CancellationToken cancellationToken) {
        if(_options.Subscriptions.Count is 0) {
            return;
        }

        var builder = _factory.CreateSubscribeOptionsBuilder();

        foreach(var subscription in _options.Subscriptions) {
            var filters = StringCompat.IsNullOrWhiteSpace(subscription.TopicFilter)
                ? _topicConvention.GetSubscriptionFilters(subscription.MessageType)
                : [subscription.TopicFilter];

            foreach(var filter in filters) {
                builder.WithTopicFilter(topicFilter => topicFilter
                    .WithTopic(filter)
                    .WithQualityOfServiceLevel(ToMqttQualityOfServiceLevel(_options.QualityOfService)));
            }
        }

        var result = await client.SubscribeAsync(builder.Build(), cancellationToken).ConfigureAwait(false);

        foreach(var item in result.Items) { // NOSONAR: Explicit loop is clearer and faster than a LINQ pipeline here.
            if(item.ResultCode is not MqttClientSubscribeResultCode.GrantedQoS0 and
                                       not MqttClientSubscribeResultCode.GrantedQoS1 and
                                       not MqttClientSubscribeResultCode.GrantedQoS2) {
                throw new MqttTransportException(
                    $"The MQTT broker rejected subscription '{item.TopicFilter.Topic}' with {item.ResultCode}.",
                    item.ResultCode.ToString());
            }
        }
    }

    private async Task PublishCoreAsync(TransportEnvelope envelope, CancellationToken cancellationToken) {
        IMqttClient client;

        lock(_sync) {
            Guard.NotDisposed(_disposed, this);

            if(!_started || _client is null || !_client.IsConnected) {
                throw new InvalidOperationException("The MQTT transport is not connected. The outbox relay can retry the message.");
            }

            client = _client;
        }

        var result = await client.PublishAsync(CreateApplicationMessage(envelope), cancellationToken).ConfigureAwait(false);

        if(!result.IsSuccess && result.ReasonCode is not MqttClientPublishReasonCode.NoMatchingSubscribers) {
            throw new MqttTransportException(
                $"The MQTT broker rejected the publish with {result.ReasonCode}. {result.ReasonString}".Trim(),
                result.ReasonCode.ToString());
        }
    }

    private async Task OnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args) {
        args.AutoAcknowledge = false;

        var message = args.ApplicationMessage;
        var envelope = CreateIncomingEnvelope(message);

        try {
            if(_handlers.Length > 0) {
                if(envelope.DeliveryMode is TransportDeliveryMode.Send) {
                    await DeliverAsync(_handlers[0], envelope).ConfigureAwait(false);
                } else {
                    foreach(var handler in _handlers) {
                        await DeliverAsync(handler, envelope).ConfigureAwait(false);
                    }
                }
            }

            if(message.QualityOfServiceLevel is not MqttQualityOfServiceLevel.AtMostOnce) {
                await args.AcknowledgeAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch(Exception error) {
            args.ProcessingFailed = true;

            if(_logger is not null) {
                _deliveryFailed(_logger, "handler", envelope.MessageId, error);
            }
        }
    }

    private async Task DeliverAsync(IMessageTransportHandler handler, TransportEnvelope envelope) {
        if(_receivePipeline is null) {
            await handler.HandleAsync(envelope, CancellationToken.None).ConfigureAwait(false);
        } else {
            await _receivePipeline.HandleAsync(handler, envelope, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args) {
        if(_stopping || !_started) {
            return Task.CompletedTask;
        }

        if(_logger is not null) {
            _disconnected(_logger, args.Reason, args.Exception);
        }

        ScheduleReconnect();

        return Task.CompletedTask;
    }

    private void ScheduleReconnect() {
        if(TransportReconnect.TryStart(ref _reconnectScheduled, _stopping, _lifetime, ReconnectLoopAsync, out var task)) {
            _reconnectTask = task;
        }
    }

    private Task ReconnectLoopAsync(CancellationToken cancellationToken) =>
        TransportReconnect.RunAsync(
            ConnectAndSubscribeAsync,
            _options.ReconnectDelay,
            _options.MaxReconnectDelay,
            error => {
                if(_logger is not null) {
                    _reconnectFailed(_logger, error);
                }
            },
            () => Interlocked.Exchange(ref _reconnectScheduled, 0),
            cancellationToken);

    private MqttApplicationMessage CreateApplicationMessage(TransportEnvelope envelope) {
        var builder = _factory.CreateApplicationMessageBuilder()
            .WithTopic(envelope.Topology.Topic)
            .WithPayload(envelope.Payload.ToArray())
            .WithQualityOfServiceLevel(ToMqttQualityOfServiceLevel(_options.QualityOfService))
            .WithRetainFlag(_options.RetainMessages)
            .WithContentType(envelope.ContentType)
            .WithUserProperty(TransportHeaders.MessageType, envelope.MessageType)
            .WithUserProperty(TransportHeaders.MessageVersion, envelope.MessageVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .WithUserProperty(MqttTransportHeaders.MessageId, envelope.MessageId)
            .WithUserProperty(MqttTransportHeaders.DeliveryMode, envelope.DeliveryMode is TransportDeliveryMode.Send ? "send" : "publish")
            .WithUserProperty(MqttTransportHeaders.Timestamp, envelope.Timestamp.ToString("O", System.Globalization.CultureInfo.InvariantCulture));

        if(!StringCompat.IsNullOrWhiteSpace(envelope.CorrelationId)) {
            builder.WithUserProperty(MqttTransportHeaders.CorrelationId, envelope.CorrelationId);
        }

        if(!StringCompat.IsNullOrWhiteSpace(envelope.CausationId)) {
            builder.WithUserProperty(MqttTransportHeaders.CausationId, envelope.CausationId);
        }

        if(!StringCompat.IsNullOrWhiteSpace(envelope.TenantId)) {
            builder.WithUserProperty(TransportHeaders.Tenant, envelope.TenantId);
        }

        if(!StringCompat.IsNullOrWhiteSpace(envelope.PartitionKey)) {
            builder.WithUserProperty(MqttTransportHeaders.PartitionKey, envelope.PartitionKey);
        }

        foreach(var pair in envelope.Headers) {
            if(IsStandardHeader(pair.Key)) {
                continue;
            }

            builder.WithUserProperty(MqttTransportHeaders.HeaderPrefix + pair.Key, pair.Value);
        }

        return builder.Build();
    }

    private TransportEnvelope CreateIncomingEnvelope(MqttApplicationMessage message) { // NOSONAR: Complexity is inherent to this state machine/pipeline and is covered by tests.
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        var userProperties = message.UserProperties ?? [];

        foreach(var property in userProperties) {
            if(property.Name.StartsWith(MqttTransportHeaders.HeaderPrefix, StringComparison.Ordinal)) {
                headers[property.Name.Substring(MqttTransportHeaders.HeaderPrefix.Length)] = property.Value;
            } else {
                headers[property.Name] = property.Value;
            }
        }

        var messageType = TransportHeaders.GetMessageType(headers);
        var resolvedType = ResolveMessageType(messageType, message.Topic);

        if(StringCompat.IsNullOrWhiteSpace(messageType)) {
            messageType = resolvedType?.AssemblyQualifiedName ?? resolvedType?.FullName;

            if(StringCompat.IsNullOrWhiteSpace(messageType)) {
                throw new FormatException("The MQTT message does not identify a message type.");
            }
        }

        var contentType = StringCompat.IsNullOrWhiteSpace(message.ContentType)
            ? TransportHeaders.GetContentType(headers) ?? "application/octet-stream"
            : message.ContentType;
        var messageVersion = TransportHeaders.GetMessageVersion(headers);
        var tenantId = headers.TryGetValue(TransportHeaders.Tenant, out var tenant) && !StringCompat.IsNullOrWhiteSpace(tenant)
            ? tenant
            : InferTenant(message.Topic, resolvedType);

        if(tenantId is not null) {
            headers[TransportHeaders.Tenant] = tenantId;
        }

        TransportHeaders.SetMetadata(headers, messageType, messageVersion, contentType);
        var timestamp = headers.TryGetValue(MqttTransportHeaders.Timestamp, out var timestampText) &&
                        DateTimeOffset.TryParse(timestampText, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : _clock.GetUtcNow();
        var deliveryMode = headers.TryGetValue(MqttTransportHeaders.DeliveryMode, out var mode) &&
                           string.Equals(mode, "send", StringComparison.OrdinalIgnoreCase)
            ? TransportDeliveryMode.Send
            : TransportDeliveryMode.Publish;
        var messageName = resolvedType is null ? message.Topic : _topicConvention.GetMessageName(resolvedType);
        var topology = new TransportTopology(_topicConvention.Prefix, message.Topic, message.Topic, messageName);

        return new TransportEnvelope(
            headers.TryGetValue(MqttTransportHeaders.MessageId, out var messageId) && !StringCompat.IsNullOrWhiteSpace(messageId)
                ? messageId
                : Guid.NewGuid().ToString("N"),
            messageType,
            contentType,
            message.PayloadSegment.ToArray(),
            ReadOptional(headers, MqttTransportHeaders.CorrelationId),
            ReadOptional(headers, MqttTransportHeaders.CausationId),
            tenantId,
            ReadOptional(headers, MqttTransportHeaders.PartitionKey),
            timestamp,
            headers,
            topology,
            deliveryMode) {
            MessageVersion = messageVersion,
        };
    }

    private Type? ResolveMessageType(string? messageType, string topic) {
        if(!StringCompat.IsNullOrWhiteSpace(messageType)) {
            var resolved = Type.GetType(messageType, throwOnError: false);

            if(resolved is not null) {
                return resolved;
            }

            foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                resolved = assembly.GetType(messageType, throwOnError: false);

                if(resolved is not null) {
                    return resolved;
                }
            }
        }

        foreach(var subscription in _options.Subscriptions) { // NOSONAR: Explicit loop is clearer and faster than a LINQ pipeline here.
            if(_topicConvention.TryGetTenant(topic, subscription.MessageType, out _)) {
                return subscription.MessageType;
            }
        }

        return null;
    }

    private string? InferTenant(string topic, Type? resolvedType) {
        if(resolvedType is not null && _topicConvention.TryGetTenant(topic, resolvedType, out var tenant)) {
            return tenant;
        }

        foreach(var subscription in _options.Subscriptions) {
            if(_topicConvention.TryGetTenant(topic, subscription.MessageType, out tenant)) {
                return tenant;
            }
        }

        return null;
    }

    private TransportEnvelope CreateEnvelope(object message, Type messageType, TransportMessageOptions? options, TransportDeliveryMode deliveryMode) =>
        _envelopes.Create(message, messageType, options, deliveryMode);

    private MqttClientOptions BuildClientOptions() {
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(_options.HostName, _options.Port)
            .WithClientId(_clientId)
            .WithCleanSession(_options.CleanSession)
            .WithKeepAlivePeriod(_options.KeepAlivePeriod)
            .WithTimeout(_options.ConnectTimeout)
            .WithProtocolVersion(MqttProtocolVersion.V500);

        if(_options.UseTls) {
            builder.WithTlsOptions(static tls => tls.UseTls());
        }

        if(!StringCompat.IsNullOrWhiteSpace(_options.UserName)) {
            builder.WithCredentials(_options.UserName, _options.Password ?? string.Empty);
        }

        _options.ConfigureClient?.Invoke(builder);

        return builder.Build();
    }

    private static bool IsStandardHeader(string name) =>
        name is TransportHeaders.MessageType or
                TransportHeaders.MessageVersion or
                TransportHeaders.ContentType or
                TransportHeaders.Tenant;

    private static string? ReadOptional(Dictionary<string, string> headers, string name) =>
        headers.TryGetValue(name, out var value) && !StringCompat.IsNullOrWhiteSpace(value) ? value : null;

    private static MqttQualityOfServiceLevel ToMqttQualityOfServiceLevel(MqttQualityOfService qualityOfService) =>
        qualityOfService switch {
            MqttQualityOfService.AtMostOnce => MqttQualityOfServiceLevel.AtMostOnce,
            MqttQualityOfService.AtLeastOnce => MqttQualityOfServiceLevel.AtLeastOnce,
            MqttQualityOfService.ExactlyOnce => MqttQualityOfServiceLevel.ExactlyOnce,
            _ => throw new ArgumentOutOfRangeException(nameof(qualityOfService), qualityOfService, "Unknown MQTT quality of service."),
        };

    private static MqttTransportOptions Validate(MqttTransportOptions options) {
        Guard.NotNull(options);

        if(StringCompat.IsNullOrWhiteSpace(options.HostName)) {
            throw new ArgumentException("HostName must not be empty.", nameof(options));
        }

        if(options.Port is <= 0 or > 65535) {
            throw new ArgumentOutOfRangeException(nameof(options), options.Port, "Port must be between 1 and 65535.");
        }

        if(!options.CleanSession && StringCompat.IsNullOrWhiteSpace(options.ClientId)) {
            throw new ArgumentException("A stable ClientId is required when CleanSession is false.", nameof(options));
        }

        if(options.KeepAlivePeriod <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options), options.KeepAlivePeriod, "KeepAlivePeriod must be greater than zero.");
        }

        if(options.ConnectTimeout <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options), options.ConnectTimeout, "ConnectTimeout must be greater than zero.");
        }

        if(options.ReconnectDelay <= TimeSpan.Zero || options.MaxReconnectDelay < options.ReconnectDelay) {
            throw new ArgumentOutOfRangeException(nameof(options), options.ReconnectDelay, "Reconnect delays must be positive and MaxReconnectDelay must be at least ReconnectDelay.");
        }

        var qualityOfService = (int)options.QualityOfService;

        if(qualityOfService is < 0 or > 2) {
            throw new ArgumentOutOfRangeException(nameof(options), options.QualityOfService, "Unknown MQTT quality of service.");
        }

        foreach(var subscription in options.Subscriptions) { // NOSONAR: Explicit loop is clearer and faster than a LINQ pipeline here.
            if(subscription.MessageType is null || subscription.MessageType == typeof(object)) {
                throw new ArgumentException("Every MQTT subscription must set a concrete MessageType.", nameof(options));
            }
        }

        return options;
    }
}
