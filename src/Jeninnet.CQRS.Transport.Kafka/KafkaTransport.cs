//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Kafka;

/// <summary>
/// Kafka <see cref="IMessageTransport"/>: topic-per-message-type, keyed partitioning, consumer
/// groups with manual offset commit after the handler succeeds, and at-least-once redelivery when
/// an offset is not committed.
/// </summary>
/// <remarks>
/// <para>
/// Send and publish both produce to the message type's topic; the envelope's delivery-mode header
/// distinguishes them. With multiple handlers each handler gets its own consumer group, so publish
/// fans out and send is handled by the first group only (the others commit without invoking their
/// handler). A single consumer per group processes messages sequentially, preserving per-key
/// ordering across the topic's partitions.
/// </para>
/// <para>
/// Offsets are committed only after the handler returns successfully. A failed handler seeks back
/// to the same offset and retries after <see cref="KafkaTransportOptions.HandlerFailureDelay"/>;
/// the offset stays uncommitted, so a restart redelivers it. This is at-least-once, not Kafka
/// transactions/exactly-once.
/// </para>
/// </remarks>
public sealed class KafkaTransport : IMessageTransport, IAsyncDisposable, IDisposable {
    private const string _messageIdHeader = "x-message-id";
    private const string _correlationHeader = "x-correlation-id";
    private const string _causationHeader = "x-causation-id";
    private const string _tenantHeader = "x-tenant-id";
    private const string _partitionKeyHeader = "x-partition-key";
    private const string _deliveryModeHeader = "x-cqrs-delivery-mode";

    private readonly KafkaTransportOptions _options;
    private readonly IMessageTransportHandler[] _handlers;
    private readonly TransportNamingConvention _namingConvention;
    private readonly ReceiveFilterPipeline? _receivePipeline;
    private readonly MessageObserverInvoker? _observers;
    private readonly Lock _sync = new();
    private readonly TransportEnvelopeBuilder _envelopes;

    private IProducer<string, byte[]>? _producer;
    private IConsumer<string, byte[]>[] _consumers = [];
    private Task[] _loops = [];
    private CancellationTokenSource? _lifetime;
    private volatile bool _started;
    private volatile bool _stopping;
    private bool _disposed;

    /// <summary>Initializes the transport from options and the registered consumer handlers.</summary>
    /// <param name="options">Producer, consumer-group, topic, and partitioning options.</param>
    /// <param name="serializer">The payload serializer used to build envelopes.</param>
    /// <param name="handlers">Consumer handlers; each gets its own consumer group.</param>
    /// <param name="namingConvention">Resolves topic names.</param>
    /// <param name="receivePipeline">Applies endpoint filters, retry, and consumption timeout; <see langword="null"/> invokes handlers directly.</param>
    /// <param name="observers">Send, publish, and consume observers; <see langword="null"/> when observation is not configured.</param>
    public KafkaTransport(
        IOptions<KafkaTransportOptions> options,
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
        _envelopes = TransportEnvelopeBuilders.ForBroker(payloadSerializer, _namingConvention.Describe);
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

        IProducer<string, byte[]>? producer = null;
        var consumers = new List<IConsumer<string, byte[]>>();
        var loops = new List<Task>();

        try {
            var topics = ResolveTopics();

            if(_options.EnsureTopics && topics.Count > 0) {
                await EnsureTopicsAsync(topics, cancellationToken).ConfigureAwait(false);
            }

            producer = new ProducerBuilder<string, byte[]>(CreateProducerConfig()).Build();

            if(_handlers.Length > 0 && topics.Count > 0) {
                for(var index = 0; index < _handlers.Length; index++) {
                    var consumer = new ConsumerBuilder<string, byte[]>(CreateConsumerConfig(index)).Build();
                    consumer.Subscribe(topics);
                    consumers.Add(consumer);

                    var handler = _handlers[index];
                    var handlerIndex = index;
                    loops.Add(Task.Run(() => ConsumeLoopAsync(consumer, handlerIndex, handler, lifetime.Token), CancellationToken.None));
                }
            }

            lock(_sync) {
                if(_stopping) {
                    throw new OperationCanceledException("The Kafka transport stopped while connecting.");
                }

                _producer = producer;
                _consumers = [.. consumers];
                _loops = [.. loops];
            }
        }
        catch {
            producer?.Dispose();

            foreach(var consumer in consumers) {
                try {
                    consumer.Close();
                }
                catch {
                    // Best effort on a failed start.
                }

                consumer.Dispose();
            }

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
        IProducer<string, byte[]>? producer;
        IConsumer<string, byte[]>[] consumers;
        Task[] loops;

        lock(_sync) {
            _stopping = true;
            _started = false;
            lifetime = _lifetime;
            producer = _producer;
            consumers = _consumers;
            loops = _loops;
            _lifetime = null;
            _producer = null;
            _consumers = [];
            _loops = [];
        }

        lifetime?.Cancel(); // NOSONAR: The synchronous API is intentional for netstandard2.0/net462 compatibility.

        try {
            if(loops.Length > 0) {
                try {
                    await Task.WhenAll(loops).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                }
                catch {
                    // Shutdown is best effort; disposing the consumers below unblocks the loops.
                }
            }
        }
        finally {
            foreach(var consumer in consumers) {
                try {
                    consumer.Close();
                }
                catch {
                    // Closing a dead consumer is best-effort.
                }

                consumer.Dispose();
            }

            producer?.Dispose();
            lifetime?.Dispose();
        }
    }

    /// <inheritdoc />
    public Task SendAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) =>
        ProduceAsync(message, messageType, options, TransportDeliveryMode.Send, cancellationToken);

    /// <inheritdoc />
    public Task PublishAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) =>
        ProduceAsync(message, messageType, options, TransportDeliveryMode.Publish, cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync() {
        lock(_sync) {
            if(_disposed) {
                return;
            }

            _disposed = true;
        }

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() =>
        DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task ProduceAsync(object message, Type messageType, TransportMessageOptions? options, TransportDeliveryMode deliveryMode, CancellationToken cancellationToken) {
        Guard.NotNull(message);
        Guard.NotNull(messageType);

        var producer = GetProducer();
        var envelope = CreateEnvelope(message, messageType, options, deliveryMode);
        var isSend = deliveryMode is TransportDeliveryMode.Send;
        var record = new Message<string, byte[]> {
            Key = envelope.PartitionKey ?? envelope.MessageId,
            Value = envelope.Payload.ToArray(),
            Headers = CreateHeaders(envelope),
            Timestamp = new Timestamp(envelope.Timestamp.UtcDateTime),
        };

        if(isSend) {
            await TransportOperation.SendAsync(
                _observers,
                message,
                messageType,
                envelope,
                ct => producer.ProduceAsync(ResolveTopic(messageType), record, ct),
                cancellationToken).ConfigureAwait(false);
        } else {
            await TransportOperation.PublishAsync(
                _observers,
                message,
                messageType,
                envelope,
                ct => producer.ProduceAsync(ResolveTopic(messageType), record, ct),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ConsumeLoopAsync(IConsumer<string, byte[]> consumer, int handlerIndex, IMessageTransportHandler handler, CancellationToken cancellationToken) { // NOSONAR: Complexity is inherent to this state machine/pipeline and is covered by tests.
        try {
            while(!cancellationToken.IsCancellationRequested) {
                ConsumeResult<string, byte[]>? result;

                try {
                    result = consumer.Consume(cancellationToken);
                }
                catch(OperationCanceledException) {
                    return;
                }
                catch(ConsumeException) {
                    await DelayAsync(cancellationToken).ConfigureAwait(false);

                    continue;
                }
                catch(KafkaException) {
                    await DelayAsync(cancellationToken).ConfigureAwait(false);

                    continue;
                }

                if(result is null || result.IsPartitionEOF) {
                    continue;
                }

                var envelope = CreateIncomingEnvelope(result);

                if(envelope.DeliveryMode is TransportDeliveryMode.Send && handlerIndex is not 0) {
                    // Send is point-to-point; every group consumes the record, only the first handles it.
                    await CommitAsync(consumer, result, cancellationToken).ConfigureAwait(false);

                    continue;
                }

                try {
                    if(_receivePipeline is null) {
                        await handler.HandleAsync(envelope, cancellationToken).ConfigureAwait(false);
                    } else {
                        await _receivePipeline.HandleAsync(handler, envelope, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
                    return;
                }
                catch(Exception) {
                    // Keep the offset uncommitted and replay the same record. A restart also
                    // redelivers it because the group's committed offset stays behind.
                    try {
                        consumer.Seek(result.TopicPartitionOffset);
                    }
                    catch(KafkaException) {
                        return;
                    }

                    await DelayAsync(cancellationToken).ConfigureAwait(false);

                    continue;
                }

                await CommitAsync(consumer, result, cancellationToken).ConfigureAwait(false);
            }
        }
        finally {
            try {
                consumer.Close();
            }
            catch {
                // Closing a dead consumer is best-effort.
            }
        }
    }

    private static async Task CommitAsync(IConsumer<string, byte[]> consumer, ConsumeResult<string, byte[]> result, CancellationToken cancellationToken) {
        while(!cancellationToken.IsCancellationRequested) {
            try {
                consumer.Commit(result);

                return;
            }
            catch(KafkaException) {
                // A commit failure must not advance the loop: retry until it succeeds or the
                // transport stops, otherwise a later commit could skip the failed record.
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task DelayAsync(CancellationToken cancellationToken) {
        try {
            await Task.Delay(_options.HandlerFailureDelay, cancellationToken).ConfigureAwait(false);
        }
        catch(OperationCanceledException) {
            // The loop observes cancellation on its next iteration.
        }
    }

    private async Task EnsureTopicsAsync(IReadOnlyList<string> topics, CancellationToken cancellationToken) {
        using var admin = new AdminClientBuilder(new AdminClientConfig {
            BootstrapServers = _options.BootstrapServers,
            ClientId = _options.ClientId,
        }).Build();

        var specifications = topics
            .Select(topic => new TopicSpecification {
                Name = topic,
                NumPartitions = _options.NumPartitions,
                ReplicationFactor = _options.ReplicationFactor,
            })
            .ToList();

        try {
            await admin.CreateTopicsAsync(specifications).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch(CreateTopicsException exception) {
            if(exception.Results.Any(static result => result.Error.Code is not ErrorCode.TopicAlreadyExists)) {
                throw;
            }
        }
    }

    private List<string> ResolveTopics() {
        var topics = new List<string>(_options.Subscriptions.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach(var subscription in _options.Subscriptions) {
            var topic = StringCompat.IsNullOrWhiteSpace(subscription.Topic)
                ? _namingConvention.Describe(subscription.MessageType).Topic
                : subscription.Topic;

            if(seen.Add(topic)) {
                topics.Add(topic);
            }
        }

        return topics;
    }

    private string ResolveTopic(Type messageType) {
        foreach(var subscription in _options.Subscriptions) {
            if(subscription.MessageType == messageType && !StringCompat.IsNullOrWhiteSpace(subscription.Topic)) {
                return subscription.Topic;
            }
        }

        return _namingConvention.Describe(messageType).Topic;
    }

    private ProducerConfig CreateProducerConfig() => new() {
        BootstrapServers = _options.BootstrapServers,
        ClientId = _options.ClientId,
        EnableIdempotence = _options.EnableIdempotence,
        Acks = Acks.All,
    };

    private ConsumerConfig CreateConsumerConfig(int handlerIndex) => new() {
        BootstrapServers = _options.BootstrapServers,
        ClientId = _options.ClientId,
        GroupId = _handlers.Length > 1 ? $"{_options.ConsumerGroup}.{handlerIndex}" : _options.ConsumerGroup,
        EnableAutoCommit = false,
        EnableAutoOffsetStore = false,
        AutoOffsetReset = AutoOffsetReset.Earliest,
        AllowAutoCreateTopics = true,
    };

    private TransportEnvelope CreateEnvelope(object message, Type messageType, TransportMessageOptions? options, TransportDeliveryMode deliveryMode) =>
        _envelopes.Create(message, messageType, options, deliveryMode);

    private static Headers CreateHeaders(TransportEnvelope envelope) {
        var headers = new Headers();

        foreach(var pair in envelope.Headers) {
            headers.Add(pair.Key, Encoding.UTF8.GetBytes(pair.Value));
        }

        return headers;
    }

    private static TransportEnvelope CreateIncomingEnvelope(ConsumeResult<string, byte[]> result) {
        var headers = ConvertHeaders(result.Message.Headers);
        var messageType = TransportHeaders.GetMessageType(headers) ?? string.Empty;
        var deliveryMode = headers.TryGetValue(_deliveryModeHeader, out var mode) && string.Equals(mode, "send", StringComparison.Ordinal)
            ? TransportDeliveryMode.Send
            : TransportDeliveryMode.Publish;
        var partitionKey = result.Message.Key ?? (headers.TryGetValue(_partitionKeyHeader, out var key) ? key : null);
        var timestamp = result.Message.Timestamp.Type is TimestampType.NotAvailable
            ? DateTimeOffset.UtcNow
            : new DateTimeOffset(result.Message.Timestamp.UtcDateTime);
        var topology = new TransportTopology(result.Topic, result.Topic, result.Topic, result.Message.Key ?? string.Empty);

        return new TransportEnvelope(
            headers.TryGetValue(_messageIdHeader, out var messageId) && !StringCompat.IsNullOrWhiteSpace(messageId) ? messageId : Guid.NewGuid().ToString("N"),
            messageType,
            TransportHeaders.GetContentType(headers) ?? string.Empty,
            result.Message.Value ?? [],
            headers.TryGetValue(_correlationHeader, out var correlationId) ? correlationId : null,
            headers.TryGetValue(_causationHeader, out var causationId) ? causationId : null,
            headers.TryGetValue(_tenantHeader, out var tenantId) ? tenantId : null,
            partitionKey,
            timestamp,
            headers,
            topology,
            deliveryMode) {
            MessageVersion = TransportHeaders.GetMessageVersion(headers),
        };
    }

    private static Dictionary<string, string> ConvertHeaders(Headers? headers) {
        var converted = new Dictionary<string, string>(StringComparer.Ordinal);

        if(headers is null) {
            return converted;
        }

        foreach(var header in headers) {
            var bytes = header.GetValueBytes();
            converted[header.Key] = bytes is null ? string.Empty : Encoding.UTF8.GetString(bytes);
        }

        return converted;
    }

    private IProducer<string, byte[]> GetProducer() {
        if(!_started || _stopping) {
            throw new InvalidOperationException("The Kafka transport is not started. Call StartAsync before publishing.");
        }

        return _producer ?? throw new InvalidOperationException("The Kafka transport producer is not available.");
    }

    private static KafkaTransportOptions Validate(KafkaTransportOptions options) {
        Guard.NotNull(options);

        if(StringCompat.IsNullOrWhiteSpace(options.BootstrapServers)) {
            throw new ArgumentException("BootstrapServers must not be empty.", nameof(options));
        }

        if(StringCompat.IsNullOrWhiteSpace(options.ConsumerGroup)) {
            throw new ArgumentException("ConsumerGroup must not be empty.", nameof(options));
        }

        if(options.NumPartitions is <= 0) {
            throw new ArgumentOutOfRangeException(nameof(options), options.NumPartitions, "NumPartitions must be greater than zero.");
        }

        if(options.ReplicationFactor is <= 0) {
            throw new ArgumentOutOfRangeException(nameof(options), options.ReplicationFactor, "ReplicationFactor must be greater than zero.");
        }

        if(options.HandlerFailureDelay < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options), options.HandlerFailureDelay, "HandlerFailureDelay must not be negative.");
        }

        foreach(var subscription in options.Subscriptions) { // NOSONAR: Explicit loop is clearer and faster than a LINQ pipeline here.
            if(subscription.MessageType is null || subscription.MessageType == typeof(object)) {
                throw new ArgumentException("Every Kafka subscription must set a concrete MessageType.", nameof(options));
            }
        }

        return options;
    }
}
