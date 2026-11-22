//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.RabbitMq;

/// <summary>
/// RabbitMQ <see cref="IMessageTransport"/>: one fan-out exchange per message type, durable
/// queues per consumer endpoint, a dead-letter exchange per queue, publisher confirms, prefetch,
/// consumer concurrency, and an owned exponential-backoff reconnect loop.
/// </summary>
/// <remarks>
/// Delivery is at-least-once: the consumer acks only after the handler completes; a rejected
/// delivery is dead-lettered. The host owns the process; the transport owns one connection and
/// its channels and rebuilds both after a broker outage without restarting the host. Publisher
/// confirms are enabled by default, so a publish during an outage throws instead of silently
/// losing an outbox row.
/// </remarks>
public sealed class RabbitMqTransport : IMessageTransport, IAsyncDisposable, IDisposable {
    private const string _causationHeader = "x-causation-id";
    private const string _tenantHeader = "x-tenant-id";
    private const string _partitionKeyHeader = "x-partition-key";
    private const string _deliveryModeHeader = "x-cqrs-delivery-mode";

    private readonly RabbitMqTransportOptions _options;
    private readonly IMessageTransportHandler[] _handlers;
    private readonly TransportNamingConvention _namingConvention;
    private readonly ReceiveFilterPipeline? _receivePipeline;
    private readonly MessageObserverInvoker? _observers;
    private readonly ConnectionFactory _factory;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly TransportEnvelopeBuilder _envelopes;

    private IConnection? _connection;
    private IChannel? _publishChannel;
    private IChannel? _consumeChannel;
    private CancellationTokenSource? _lifetime;
    private Task? _reconnectTask;
    private volatile bool _started;
    private volatile bool _stopping;
    private bool _disposed;
    private int _reconnectScheduled;

    /// <summary>Initializes the transport from options and the registered consumer handlers.</summary>
    /// <param name="options">Connection, topology, and consumption options.</param>
    /// <param name="serializer">The payload serializer used to build envelopes.</param>
    /// <param name="handlers">Consumer handlers; each handler gets its own queue per subscription.</param>
    /// <param name="namingConvention">Resolves exchange/queue/topic names.</param>
    /// <param name="receivePipeline">Applies endpoint filters, retry, and consumption timeout; <see langword="null"/> invokes handlers directly.</param>
    /// <param name="observers">Send, publish, and consume observers; <see langword="null"/> when observation is not configured.</param>
    public RabbitMqTransport(
        IOptions<RabbitMqTransportOptions> options,
        IMessageSerializer serializer,
        IEnumerable<IMessageTransportHandler> handlers,
        TransportNamingConvention namingConvention,
        ReceiveFilterPipeline? receivePipeline = null,
        MessageObserverInvoker? observers = null) {
        _options = Validate((options ?? throw new ArgumentNullException(nameof(options))).Value);
        var payloadSerializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _handlers = handlers?.ToArray() ?? [];
        _namingConvention = namingConvention ?? throw new ArgumentNullException(nameof(namingConvention));
        _receivePipeline = receivePipeline;
        _observers = observers;
        _envelopes = TransportEnvelopeBuilders.ForBroker(
            payloadSerializer,
            _namingConvention.Describe,
            partitionFallsBackToTenant: false);

        _factory = new ConnectionFactory {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            ClientProvidedName = _options.ClientProvidedName,
            ConsumerDispatchConcurrency = _options.ConsumerDispatchConcurrency,

            // The transport owns reconnection so the backoff is observable and testable;
            // client-side automatic recovery would race this loop.
            AutomaticRecoveryEnabled = false,
            TopologyRecoveryEnabled = false,
        };
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
            await ConnectAsync(linked.Token).ConfigureAwait(false);
        }
        catch {
            lock(_sync) {
                _started = false;
                _stopping = true;
                _lifetime?.Dispose();
                _lifetime = null;
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
            // The caller's shutdown budget is exhausted; dispose what we can without the gate.
        }

        try {
            var connection = _connection;
            var publish = _publishChannel;
            var consume = _consumeChannel;
            _connection = null;
            _publishChannel = null;
            _consumeChannel = null;

            if(connection is not null) {
                connection.ConnectionShutdownAsync -= OnConnectionShutdownAsync;
            }

            await DisposeChannelAsync(consume).ConfigureAwait(false);
            await DisposeChannelAsync(publish).ConfigureAwait(false);

            if(connection is not null) {
                try {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
                catch {
                    // Closing a broker connection is best-effort during shutdown.
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

        var topology = _namingConvention.Describe(messageType);
        var envelope = CreateEnvelope(message, messageType, options, topology, TransportDeliveryMode.Send);
        var channel = GetPublishChannel();

        await TransportOperation.SendAsync(
            _observers,
            message,
            messageType,
            envelope,
            ct => channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: topology.Queue,
                mandatory: true,
                basicProperties: CreateProperties(envelope),
                body: envelope.Payload,
                cancellationToken: ct).AsTask(),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PublishAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) {
        Guard.NotNull(message);
        Guard.NotNull(messageType);

        var topology = _namingConvention.Describe(messageType);
        var envelope = CreateEnvelope(message, messageType, options, topology, TransportDeliveryMode.Publish);
        var channel = GetPublishChannel();

        await TransportOperation.PublishAsync(
            _observers,
            message,
            messageType,
            envelope,
            ct => channel.BasicPublishAsync(
                exchange: topology.Exchange,
                routingKey: topology.RoutingKey,
                mandatory: true,
                basicProperties: CreateProperties(envelope),
                body: envelope.Payload,
                cancellationToken: ct).AsTask(),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() {
        lock(_sync) {
            if(_disposed) {
                return;
            }

            _disposed = true;
        }

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _connectGate.Dispose();
    }

    /// <inheritdoc />
    public void Dispose() =>
        DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task ConnectAsync(CancellationToken cancellationToken) {
        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try {
            await DisposeCurrentResourcesAsync().ConfigureAwait(false);

            var connection = await _factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            IChannel? publish = null;
            IChannel? consume = null;

            try {
                publish = await connection.CreateChannelAsync(
                    new CreateChannelOptions(_options.PublisherConfirms, _options.PublisherConfirms),
                    cancellationToken).ConfigureAwait(false);

                if(_options.Subscriptions.Count > 0) {
                    consume = await connection.CreateChannelAsync(
                        new CreateChannelOptions(false, false, null, _options.ConsumerDispatchConcurrency),
                        cancellationToken).ConfigureAwait(false);
                    await consume.BasicQosAsync(0, _options.PrefetchCount, false, cancellationToken).ConfigureAwait(false);

                    foreach(var subscription in _options.Subscriptions) {
                        await DeclareAndConsumeAsync(consume, subscription, cancellationToken).ConfigureAwait(false);
                    }
                }

                connection.ConnectionShutdownAsync += OnConnectionShutdownAsync;

                if(_stopping) {
                    throw new OperationCanceledException("The RabbitMQ transport stopped while connecting.");
                }

                lock(_sync) {
                    _connection = connection;
                    _publishChannel = publish;
                    _consumeChannel = consume;
                }
            }
            catch {
                await DisposeChannelAsync(consume).ConfigureAwait(false);
                await DisposeChannelAsync(publish).ConfigureAwait(false);

                try {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
                catch {
                    // Preserve the original connect/declare failure.
                }

                throw;
            }
        }
        finally {
            _connectGate.Release();
        }
    }

    private async Task DeclareAndConsumeAsync(IChannel channel, RabbitMqSubscription subscription, CancellationToken cancellationToken) {
        var topology = _namingConvention.Describe(subscription.MessageType);

        if(!StringCompat.IsNullOrWhiteSpace(subscription.QueueName)) {
            topology = topology with { Queue = subscription.QueueName };
        }

        await channel.ExchangeDeclareAsync(
            exchange: topology.Exchange,
            type: _options.ExchangeType,
            durable: _options.Durable,
            autoDelete: false,
            arguments: null,
            passive: false,
            noWait: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if(_handlers.Length is 0) {
            await DeclareQueueAsync(channel, topology, topology.Queue, cancellationToken).ConfigureAwait(false);

            return;
        }

        for(var index = 0; index < _handlers.Length; index++) {
            var queue = index is 0 ? topology.Queue : $"{topology.Queue}.{index}";
            await DeclareQueueAsync(channel, topology, queue, cancellationToken).ConfigureAwait(false);

            var handler = _handlers[index];
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, args) => HandleDeliveryAsync(channel, handler, args, topology);

            await channel.BasicConsumeAsync(
                queue: queue,
                autoAck: false,
                consumer: consumer,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DeclareQueueAsync(IChannel channel, TransportTopology topology, string queue, CancellationToken cancellationToken) {
        Dictionary<string, object?>? arguments = null;

        if(_options.EnableDeadLetter) {
            var deadLetterExchange = $"{topology.Exchange}.dlx";
            var deadLetterQueue = $"{queue}.dlq";

            await channel.ExchangeDeclareAsync(
                exchange: deadLetterExchange,
                type: "direct",
                durable: _options.Durable,
                autoDelete: false,
                arguments: null,
                passive: false,
                noWait: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await channel.QueueDeclareAsync(
                queue: deadLetterQueue,
                durable: _options.Durable,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                passive: false,
                noWait: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await channel.QueueBindAsync(
                queue: deadLetterQueue,
                exchange: deadLetterExchange,
                routingKey: deadLetterQueue,
                arguments: null,
                noWait: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            arguments = new Dictionary<string, object?>(StringComparer.Ordinal) {
                ["x-dead-letter-exchange"] = deadLetterExchange,
                ["x-dead-letter-routing-key"] = deadLetterQueue,
            };
        }

        if(_options.UseQuorumQueues) {
            arguments ??= new Dictionary<string, object?>(StringComparer.Ordinal);
            arguments["x-queue-type"] = "quorum";
        }

        await channel.QueueDeclareAsync(
            queue: queue,
            durable: _options.Durable,
            exclusive: false,
            autoDelete: false,
            arguments: arguments,
            passive: false,
            noWait: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await channel.QueueBindAsync(
            queue: queue,
            exchange: topology.Exchange,
            routingKey: topology.RoutingKey,
            arguments: null,
            noWait: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleDeliveryAsync(IChannel channel, IMessageTransportHandler handler, BasicDeliverEventArgs args, TransportTopology topology) {
        var envelope = CreateIncomingEnvelope(args, topology);

        try {
            if(_receivePipeline is null) {
                await handler.HandleAsync(envelope, CancellationToken.None).ConfigureAwait(false);
            } else {
                await _receivePipeline.HandleAsync(handler, envelope, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch {
            try {
                await channel.BasicNackAsync(args.DeliveryTag, false, false, CancellationToken.None).ConfigureAwait(false);
            }
            catch {
                // Closing channels redeliver unacked messages; the dead-letter path is best-effort.
            }

            return;
        }

        try {
            await channel.BasicAckAsync(args.DeliveryTag, false, CancellationToken.None).ConfigureAwait(false);
        }
        catch {
            // A lost ack redelivers the message (at-least-once); the handler already ran.
        }
    }

    private TransportEnvelope CreateEnvelope(object message, Type messageType, TransportMessageOptions? options, TransportTopology topology, TransportDeliveryMode deliveryMode) =>
        _envelopes.Create(message, messageType, options, deliveryMode, topology);

    private BasicProperties CreateProperties(TransportEnvelope envelope) {
        var headers = new Dictionary<string, object?>(envelope.Headers.Count, StringComparer.Ordinal);

        foreach(var pair in envelope.Headers) {
            headers[pair.Key] = pair.Value;
        }

        return new BasicProperties {
            MessageId = envelope.MessageId,
            Type = envelope.MessageType,
            ContentType = envelope.ContentType,
            ContentEncoding = "utf-8",
            CorrelationId = envelope.CorrelationId,
            Timestamp = new AmqpTimestamp(envelope.Timestamp.ToUnixTimeSeconds()),
            DeliveryMode = _options.Durable ? DeliveryModes.Persistent : DeliveryModes.Transient,
            Headers = headers,
        };
    }

    private static TransportEnvelope CreateIncomingEnvelope(BasicDeliverEventArgs args, TransportTopology topology) {
        var properties = args.BasicProperties;
        var headers = ConvertHeaders(properties.Headers);
        var timestamp = properties.Timestamp.UnixTime > 0
            ? DateTimeOffset.FromUnixTimeSeconds(properties.Timestamp.UnixTime)
            : DateTimeOffset.UtcNow;
        var deliveryMode = headers.TryGetValue(_deliveryModeHeader, out var mode) && string.Equals(mode, "send", StringComparison.Ordinal)
            ? TransportDeliveryMode.Send
            : TransportDeliveryMode.Publish;
        var messageType = TransportHeaders.GetMessageType(headers) ?? properties.Type ?? string.Empty;
        var contentType = TransportHeaders.GetContentType(headers) ?? properties.ContentType ?? string.Empty;

        return new TransportEnvelope(
            StringCompat.IsNullOrWhiteSpace(properties.MessageId) ? Guid.NewGuid().ToString("N") : properties.MessageId,
            messageType,
            contentType,
            args.Body.ToArray(),
            properties.CorrelationId,
            headers.TryGetValue(_causationHeader, out var causation) ? causation : null,
            headers.TryGetValue(_tenantHeader, out var tenant) ? tenant : null,
            headers.TryGetValue(_partitionKeyHeader, out var partitionKey) ? partitionKey : null,
            timestamp,
            headers,
            topology,
            deliveryMode) {
            MessageVersion = TransportHeaders.GetMessageVersion(headers),
        };
    }

    private static Dictionary<string, string> ConvertHeaders(IDictionary<string, object?>? headers) {
        var converted = new Dictionary<string, string>(headers?.Count ?? 0, StringComparer.Ordinal);

        if(headers is null) {
            return converted;
        }

        foreach(var pair in headers) {
            converted[pair.Key] = pair.Value switch {
                null => string.Empty,
                string text => text,
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                _ => Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            };
        }

        return converted;
    }

    private IChannel GetPublishChannel() {
        if(!_started || _stopping) {
            throw new InvalidOperationException("The RabbitMQ transport is not started. Call StartAsync before publishing.");
        }

        var channel = _publishChannel;

        if(channel is null || !channel.IsOpen) {
            throw new InvalidOperationException("The RabbitMQ transport is reconnecting; the message was not published.");
        }

        return channel;
    }

    private Task OnConnectionShutdownAsync(object sender, ShutdownEventArgs args) {
        if(_stopping) {
            return Task.CompletedTask;
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
            ConnectAsync,
            _options.ReconnectDelay,
            _options.MaxReconnectDelay,
            onFailure: null,
            () => Interlocked.Exchange(ref _reconnectScheduled, 0),
            cancellationToken);

    private async Task DisposeCurrentResourcesAsync() {
        IConnection? connection;
        IChannel? publish;
        IChannel? consume;

        lock(_sync) {
            connection = _connection;
            publish = _publishChannel;
            consume = _consumeChannel;
            _connection = null;
            _publishChannel = null;
            _consumeChannel = null;
        }

        if(connection is not null) {
            connection.ConnectionShutdownAsync -= OnConnectionShutdownAsync;

            try {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch {
                // Disposing a dead connection is best-effort.
            }
        }

        await DisposeChannelAsync(consume).ConfigureAwait(false);
        await DisposeChannelAsync(publish).ConfigureAwait(false);
    }

    private static async Task DisposeChannelAsync(IChannel? channel) {
        if(channel is null) {
            return;
        }

        try {
            await channel.DisposeAsync().ConfigureAwait(false);
        }
        catch {
            // Disposing a dead channel is best-effort.
        }
    }

    private static RabbitMqTransportOptions Validate(RabbitMqTransportOptions options) {
        Guard.NotNull(options);

        if(StringCompat.IsNullOrWhiteSpace(options.HostName)) {
            throw new ArgumentException("HostName must not be empty.", nameof(options));
        }

        if(options.Port is <= 0 or > 65535) {
            throw new ArgumentOutOfRangeException(nameof(options), options.Port, "Port must be between 1 and 65535.");
        }

        if(StringCompat.IsNullOrWhiteSpace(options.UserName) || StringCompat.IsNullOrWhiteSpace(options.Password)) {
            throw new ArgumentException("UserName and Password must not be empty.", nameof(options));
        }

        if(options.PrefetchCount is 0) {
            throw new ArgumentOutOfRangeException(nameof(options), options.PrefetchCount, "PrefetchCount must be greater than zero.");
        }

        if(options.ConsumerDispatchConcurrency is 0) {
            throw new ArgumentOutOfRangeException(nameof(options), options.ConsumerDispatchConcurrency, "ConsumerDispatchConcurrency must be greater than zero.");
        }

        if(options.ReconnectDelay <= TimeSpan.Zero || options.MaxReconnectDelay < options.ReconnectDelay) {
            throw new ArgumentOutOfRangeException(nameof(options), options.ReconnectDelay, "Reconnect delays must be positive and MaxReconnectDelay must be at least ReconnectDelay.");
        }

        foreach(var subscription in options.Subscriptions) { // NOSONAR: Explicit loop is clearer and faster than a LINQ pipeline here.
            if(subscription.MessageType is null || subscription.MessageType == typeof(object)) {
                throw new ArgumentException("Every RabbitMQ subscription must set a concrete MessageType.", nameof(options));
            }
        }

        return options;
    }
}
