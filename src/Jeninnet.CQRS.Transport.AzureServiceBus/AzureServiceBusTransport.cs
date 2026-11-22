//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.AzureServiceBus;

/// <summary>
/// Azure Service Bus <see cref="IMessageTransport"/>: queue-per-message-type for sends,
/// topic/subscription-per-consumer for publishes, optional sessions for ordered handling,
/// native scheduled enqueue, and dead-letter forwarding to the CQRS dead-letter store.
/// </summary>
/// <remarks>
/// The adapter owns all Azure SDK objects; core sees only canonical envelopes and handlers.
/// Receivers use PeekLock and complete only after the handler succeeds. A handler failure records
/// the original envelope (including headers) in <see cref="IDeadLetterStore"/> and moves the
/// broker message to the entity's dead-letter subqueue. At-least-once is documented; duplicate
/// detection is deliberately not relied on because idempotency is owned by CQRS.
/// </remarks>
public sealed class AzureServiceBusTransport : IMessageTransport, IAsyncDisposable, IDisposable {
    private const string _messageIdHeader = "x-message-id";
    private const string _correlationHeader = "x-correlation-id";
    private const string _causationHeader = "x-causation-id";
    private const string _tenantHeader = "x-tenant-id";
    private const string _partitionKeyHeader = "x-partition-key";
    private const string _deliveryModeHeader = "x-cqrs-delivery-mode";

    private readonly AzureServiceBusTransportOptions _options;
    private readonly IMessageTransportHandler[] _handlers;
    private readonly TransportNamingConvention _namingConvention;
    private readonly IDeadLetterStore _deadLetters;
    private readonly ReceiveFilterPipeline? _receivePipeline;
    private readonly MessageObserverInvoker? _observers;
    private readonly Lock _sync = new();
    private readonly TransportEnvelopeBuilder _envelopes;
    private readonly Dictionary<string, ServiceBusSender> _senders = new(StringComparer.Ordinal);

    private ServiceBusClient? _client;
    private CancellationTokenSource? _lifetime;
    private Task[] _loops = [];
    private volatile bool _started;
    private volatile bool _stopping;
    private bool _disposed;

    /// <summary>Initializes the transport from options and the registered consumer handlers.</summary>
    /// <param name="options">Authentication, topology, session, and receiver options.</param>
    /// <param name="serializer">The payload serializer used to build envelopes.</param>
    /// <param name="handlers">Consumer handlers; the first handles sends, all handle publishes.</param>
    /// <param name="namingConvention">Resolves queue/topic names.</param>
    /// <param name="deadLetters">Receives a copy of every broker-dead-lettered message.</param>
    /// <param name="receivePipeline">Applies endpoint filters, retry, and consumption timeout; <see langword="null"/> invokes handlers directly.</param>
    /// <param name="observers">Send, publish, and consume observers; <see langword="null"/> when observation is not configured.</param>
    public AzureServiceBusTransport(
        IOptions<AzureServiceBusTransportOptions> options,
        IMessageSerializer serializer,
        IEnumerable<IMessageTransportHandler> handlers,
        TransportNamingConvention namingConvention,
        IDeadLetterStore deadLetters,
        ReceiveFilterPipeline? receivePipeline = null,
        MessageObserverInvoker? observers = null) {
        _options = Validate((options ?? throw new ArgumentNullException(nameof(options))).Value);
        var payloadSerializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _handlers = handlers?.ToArray() ?? [];
        _namingConvention = namingConvention ?? throw new ArgumentNullException(nameof(namingConvention));
        _deadLetters = deadLetters ?? throw new ArgumentNullException(nameof(deadLetters));
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

        ServiceBusClient? client = null;
        var loops = new List<Task>();

        try {
            client = CreateClient();

            if(_options.EnsureTopology) {
                await EnsureTopologyAsync(cancellationToken).ConfigureAwait(false);
            }

            if(_handlers.Length > 0) {
                foreach(var subscription in _options.Subscriptions) { // NOSONAR: Explicit loop is clearer and faster than a LINQ pipeline here.
                    var queue = ResolveQueue(subscription.MessageType);
                    var topic = ResolveTopic(subscription.MessageType);
                    loops.Add(Task.Run(() => ReceiveQueueLoopAsync(client, queue, _handlers[0], lifetime.Token), CancellationToken.None));

                    for(var index = 0; index < _handlers.Length; index++) {
                        var handlerIndex = index;
                        var handler = _handlers[index];
                        var subscriptionName = SubscriptionName(handlerIndex);
                        loops.Add(Task.Run(() => ReceiveSubscriptionLoopAsync(client, topic, subscriptionName, handler, lifetime.Token), CancellationToken.None));
                    }
                }
            }

            lock(_sync) {
                if(_stopping) {
                    throw new OperationCanceledException("The Azure Service Bus transport stopped while connecting.");
                }

                _client = client;
                _loops = [.. loops];
            }
        }
        catch {
            if(client is not null) {
                try {
                    await client.DisposeAsync().ConfigureAwait(false);
                }
                catch {
                    // Preserve the startup failure.
                }
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
        ServiceBusClient? client;
        Task[] loops;
        ServiceBusSender[] senders;

        lock(_sync) {
            _stopping = true;
            _started = false;
            lifetime = _lifetime;
            client = _client;
            loops = _loops;
            senders = [.. _senders.Values];
            _senders.Clear();
            _lifetime = null;
            _client = null;
            _loops = [];
        }

        lifetime?.Cancel(); // NOSONAR: The synchronous API is intentional for netstandard2.0/net462 compatibility.

        try {
            if(loops.Length > 0) {
                try {
                    await Task.WhenAll(loops).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                }
                catch {
                    // Shutdown is best effort; disposing the client below unblocks the loops.
                }
            }
        }
        finally {
            foreach(var sender in senders) {
                try {
                    await sender.DisposeAsync().ConfigureAwait(false);
                }
                catch {
                    // Best effort.
                }
            }

            if(client is not null) {
                try {
                    await client.DisposeAsync().ConfigureAwait(false);
                }
                catch {
                    // Best effort.
                }
            }

            lifetime?.Dispose();
        }
    }

    /// <inheritdoc />
    public Task SendAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) =>
        SendCoreAsync(ResolveQueue(messageType), message, messageType, options, TransportDeliveryMode.Send, cancellationToken);

    /// <inheritdoc />
    public Task PublishAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) =>
        SendCoreAsync(ResolveTopic(messageType), message, messageType, options, TransportDeliveryMode.Publish, cancellationToken);

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

    private async Task SendCoreAsync(string entity, object message, Type messageType, TransportMessageOptions? options, TransportDeliveryMode deliveryMode, CancellationToken cancellationToken) {
        Guard.NotNull(message);
        Guard.NotNull(messageType);

        var sender = GetSender(entity);
        var envelope = CreateEnvelope(message, messageType, options, deliveryMode);
        var isSend = deliveryMode is TransportDeliveryMode.Send;
        var serviceBusMessage = new ServiceBusMessage(BinaryData.FromBytes(envelope.Payload.ToArray())) {
            MessageId = envelope.MessageId,
            CorrelationId = envelope.CorrelationId,
            ContentType = envelope.ContentType,
            Subject = envelope.MessageType,
        };

        foreach(var pair in envelope.Headers) {
            serviceBusMessage.ApplicationProperties[pair.Key] = pair.Value;
        }

        if(_options.SessionsEnabled) {
            serviceBusMessage.SessionId = envelope.PartitionKey ?? envelope.MessageId;
        }

        if(options?.ScheduledEnqueueTimeUtc is { } scheduled) {
            serviceBusMessage.ScheduledEnqueueTime = scheduled;
        }

        if(isSend) {
            await TransportOperation.SendAsync(
                _observers,
                message,
                messageType,
                envelope,
                ct => sender.SendMessageAsync(serviceBusMessage, ct),
                cancellationToken).ConfigureAwait(false);
        } else {
            await TransportOperation.PublishAsync(
                _observers,
                message,
                messageType,
                envelope,
                ct => sender.SendMessageAsync(serviceBusMessage, ct),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReceiveQueueLoopAsync(ServiceBusClient client, string queue, IMessageTransportHandler handler, CancellationToken cancellationToken) {
        await ReceiveLoopAsync(client, queue, topic: null, subscription: null, handler, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReceiveSubscriptionLoopAsync(ServiceBusClient client, string topic, string subscription, IMessageTransportHandler handler, CancellationToken cancellationToken) {
        await ReceiveLoopAsync(client, queue: null, topic, subscription, handler, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(ServiceBusClient client, string? queue, string? topic, string? subscription, IMessageTransportHandler handler, CancellationToken cancellationToken) {
        if(_options.SessionsEnabled) {
            await ReceiveSessionsAsync(client, queue, topic, subscription, handler, cancellationToken).ConfigureAwait(false);

            return;
        }

        var receiverOptions = new ServiceBusReceiverOptions {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            PrefetchCount = _options.PrefetchCount,
        };

        await using var receiver = queue is not null
            ? client.CreateReceiver(queue, receiverOptions)
            : client.CreateReceiver(topic!, subscription!, receiverOptions);

        while(!cancellationToken.IsCancellationRequested) {
            ServiceBusReceivedMessage? message;

            try {
                message = await receiver.ReceiveMessageAsync(_options.ReceiveWaitTime, cancellationToken).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
                return;
            }
            catch(ServiceBusException exception) when(exception.IsTransient) {
                await DelayAsync(cancellationToken).ConfigureAwait(false);

                continue;
            }

            if(message is not null) {
                await ProcessAsync(receiver, message, queue is not null, queue, topic, subscription, handler, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ReceiveSessionsAsync(ServiceBusClient client, string? queue, string? topic, string? subscription, IMessageTransportHandler handler, CancellationToken cancellationToken) { // NOSONAR: Complexity is inherent to this state machine/pipeline and is covered by tests.
        var options = new ServiceBusSessionReceiverOptions {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            PrefetchCount = _options.PrefetchCount,
        };

        while(!cancellationToken.IsCancellationRequested) {
            ServiceBusSessionReceiver? receiver = null;

            try {
                receiver = queue is not null
                    ? await client.AcceptNextSessionAsync(queue, options, cancellationToken).ConfigureAwait(false)
                    : await client.AcceptNextSessionAsync(topic!, subscription!, options, cancellationToken).ConfigureAwait(false);

                while(!cancellationToken.IsCancellationRequested) {
                    var message = await receiver.ReceiveMessageAsync(_options.ReceiveWaitTime, cancellationToken).ConfigureAwait(false);

                    if(message is null) {
                        break; // Release the session; another session may be waiting.
                    }

                    await ProcessAsync(receiver, message, queue is not null, queue, topic, subscription, handler, cancellationToken).ConfigureAwait(false);
                }
            }
            catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
                return;
            }
            catch(ServiceBusException exception) when(exception.Reason is ServiceBusFailureReason.ServiceTimeout or ServiceBusFailureReason.SessionLockLost || exception.IsTransient) {
                await DelayAsync(cancellationToken).ConfigureAwait(false);
            }
            finally {
                if(receiver is not null) {
                    try {
                        await receiver.DisposeAsync().ConfigureAwait(false);
                    }
                    catch {
                        // Best effort; a lost session lock is expected during restart.
                    }
                }
            }
        }
    }

    private async Task ProcessAsync( // NOSONAR: Public options/records intentionally group related settings; splitting would fragment the API.
        ServiceBusReceiver receiver,
        ServiceBusReceivedMessage message,
        bool isQueue,
        string? queue,
        string? topic,
        string? subscription,
        IMessageTransportHandler handler,
        CancellationToken cancellationToken) {
        var envelope = CreateIncomingEnvelope(message, isQueue, queue, topic, subscription);

        try {
            if(_receivePipeline is null) {
                await handler.HandleAsync(envelope, cancellationToken).ConfigureAwait(false);
            } else {
                await _receivePipeline.HandleAsync(handler, envelope, cancellationToken).ConfigureAwait(false);
            }

            await receiver.CompleteMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch(Exception exception) {
            try {
                await _deadLetters.RecordAsync(new TransportEnvelopeDeferred(envelope), exception, DateTimeOffset.UtcNow, CancellationToken.None).ConfigureAwait(false);
            }
            catch {
                // The broker dead-letter below remains the durable record.
            }

            try {
                await receiver.DeadLetterMessageAsync(message, "handler-failure", exception.Message, CancellationToken.None).ConfigureAwait(false);
            }
            catch {
                try {
                    await receiver.AbandonMessageAsync(message, cancellationToken: CancellationToken.None).ConfigureAwait(false);
                }
                catch {
                    // Lock expiry redelivers; at-least-once remains.
                }
            }
        }
    }

    private TransportEnvelope CreateEnvelope(object message, Type messageType, TransportMessageOptions? options, TransportDeliveryMode deliveryMode) =>
        _envelopes.Create(message, messageType, options, deliveryMode);

    private static TransportEnvelope CreateIncomingEnvelope(ServiceBusReceivedMessage message, bool isQueue, string? queue, string? topic, string? subscription) {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach(var pair in message.ApplicationProperties) {
            headers[pair.Key] = pair.Value switch {
                null => string.Empty,
                string text => text,
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                _ => Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            };
        }

        var messageType = TransportHeaders.GetMessageType(headers) ?? message.Subject ?? string.Empty;
        var deliveryMode = headers.TryGetValue(_deliveryModeHeader, out var mode) && string.Equals(mode, "send", StringComparison.Ordinal)
            ? TransportDeliveryMode.Send
            : TransportDeliveryMode.Publish;
        var partitionKey = message.SessionId ?? (headers.TryGetValue(_partitionKeyHeader, out var partitionHeader) ? partitionHeader : null);
        var topology = isQueue
            ? new TransportTopology(queue ?? string.Empty, queue ?? string.Empty, queue ?? string.Empty, queue ?? string.Empty)
            : new TransportTopology(topic ?? string.Empty, subscription ?? string.Empty, topic ?? string.Empty, topic ?? string.Empty);

        return new TransportEnvelope(
            headers.TryGetValue(_messageIdHeader, out var messageId) && !StringCompat.IsNullOrWhiteSpace(messageId) ? messageId : message.MessageId ?? Guid.NewGuid().ToString("N"),
            messageType,
            message.ContentType ?? TransportHeaders.GetContentType(headers) ?? string.Empty,
            message.Body.ToArray(),
            message.CorrelationId ?? (headers.TryGetValue(_correlationHeader, out var correlation) ? correlation : null),
            headers.TryGetValue(_causationHeader, out var causation) ? causation : null,
            headers.TryGetValue(_tenantHeader, out var tenant) ? tenant : null,
            partitionKey,
            message.EnqueuedTime,
            headers,
            topology,
            deliveryMode) {
            MessageVersion = TransportHeaders.GetMessageVersion(headers),
        };
    }

    private async Task EnsureTopologyAsync(CancellationToken cancellationToken) {
        var admin = CreateAdministrationClient();

        foreach(var subscription in _options.Subscriptions) { // NOSONAR: Explicit loop is clearer and faster than a LINQ pipeline here.
            var queue = ResolveQueue(subscription.MessageType);
            var topic = ResolveTopic(subscription.MessageType);

            await CreateQueueAsync(admin, queue, cancellationToken).ConfigureAwait(false);
            await CreateTopicAsync(admin, topic, cancellationToken).ConfigureAwait(false);

            for(var index = 0; index < Math.Max(_handlers.Length, 1); index++) {
                await CreateSubscriptionAsync(admin, topic, SubscriptionName(index), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task CreateQueueAsync(ServiceBusAdministrationClient admin, string queue, CancellationToken cancellationToken) {
        try {
            await admin.CreateQueueAsync(new CreateQueueOptions(queue) {
                RequiresSession = _options.SessionsEnabled,
                LockDuration = _options.LockDuration,
                MaxDeliveryCount = 10,
            }, cancellationToken).ConfigureAwait(false);
        }
        catch(ServiceBusException exception) when(exception.Reason is ServiceBusFailureReason.MessagingEntityAlreadyExists) {
            // Idempotent topology creation: the entity already exists.
        }
    }

    private static async Task CreateTopicAsync(ServiceBusAdministrationClient admin, string topic, CancellationToken cancellationToken) {
        try {
            await admin.CreateTopicAsync(new CreateTopicOptions(topic), cancellationToken).ConfigureAwait(false);
        }
        catch(ServiceBusException exception) when(exception.Reason is ServiceBusFailureReason.MessagingEntityAlreadyExists) {
            // Idempotent topology creation: the entity already exists.
        }
    }

    private async Task CreateSubscriptionAsync(ServiceBusAdministrationClient admin, string topic, string subscription, CancellationToken cancellationToken) {
        try {
            await admin.CreateSubscriptionAsync(new CreateSubscriptionOptions(topic, subscription) {
                RequiresSession = _options.SessionsEnabled,
                LockDuration = _options.LockDuration,
                MaxDeliveryCount = 10,
            }, cancellationToken).ConfigureAwait(false);
        }
        catch(ServiceBusException exception) when(exception.Reason is ServiceBusFailureReason.MessagingEntityAlreadyExists) {
            // Idempotent topology creation: the entity already exists.
        }
    }

    private ServiceBusClient CreateClient() =>
        !StringCompat.IsNullOrWhiteSpace(_options.ConnectionString)
            ? new ServiceBusClient(_options.ConnectionString)
            : new ServiceBusClient(_options.FullyQualifiedNamespace!, _options.Credential!);

    private ServiceBusAdministrationClient CreateAdministrationClient() =>
        !StringCompat.IsNullOrWhiteSpace(_options.ConnectionString)
            ? new ServiceBusAdministrationClient(_options.ConnectionString)
            : new ServiceBusAdministrationClient(_options.FullyQualifiedNamespace!, _options.Credential!);

    private ServiceBusSender GetSender(string entity) {
        lock(_sync) {
            if(!_senders.TryGetValue(entity, out var sender)) {
                sender = (_client ?? throw new InvalidOperationException("The Azure Service Bus transport is not started.")).CreateSender(entity);
                _senders.Add(entity, sender);
            }

            return sender;
        }
    }

    private string ResolveQueue(Type messageType) {
        foreach(var subscription in _options.Subscriptions) {
            if(subscription.MessageType == messageType && !StringCompat.IsNullOrWhiteSpace(subscription.QueueName)) {
                return subscription.QueueName;
            }
        }

        return _namingConvention.Describe(messageType).Queue;
    }

    private string ResolveTopic(Type messageType) {
        foreach(var subscription in _options.Subscriptions) {
            if(subscription.MessageType == messageType && !StringCompat.IsNullOrWhiteSpace(subscription.TopicName)) {
                return subscription.TopicName;
            }
        }

        return _namingConvention.Describe(messageType).Topic;
    }

    private string SubscriptionName(int handlerIndex) =>
        handlerIndex is 0 ? _options.SubscriptionPrefix : $"{_options.SubscriptionPrefix}.{handlerIndex}";

    private static async Task DelayAsync(CancellationToken cancellationToken) {
        try {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
        catch(OperationCanceledException) {
            // The loop observes cancellation on its next iteration.
        }
    }

    private static AzureServiceBusTransportOptions Validate(AzureServiceBusTransportOptions options) {
        Guard.NotNull(options);

        var hasConnectionString = !StringCompat.IsNullOrWhiteSpace(options.ConnectionString);
        var hasManagedIdentity = !StringCompat.IsNullOrWhiteSpace(options.FullyQualifiedNamespace) && options.Credential is not null;

        if(hasConnectionString == hasManagedIdentity) {
            throw new ArgumentException("Configure exactly one of ConnectionString or FullyQualifiedNamespace + Credential.", nameof(options));
        }

        if(StringCompat.IsNullOrWhiteSpace(options.SubscriptionPrefix)) {
            throw new ArgumentException("SubscriptionPrefix must not be empty.", nameof(options));
        }

        if(options.PrefetchCount < 0) {
            throw new ArgumentOutOfRangeException(nameof(options), options.PrefetchCount, "PrefetchCount must not be negative.");
        }

        if(options.ReceiveWaitTime <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options), options.ReceiveWaitTime, "ReceiveWaitTime must be greater than zero.");
        }

        if(options.LockDuration <= TimeSpan.Zero || options.LockDuration > TimeSpan.FromMinutes(5)) {
            throw new ArgumentOutOfRangeException(nameof(options), options.LockDuration, "LockDuration must be between zero and five minutes.");
        }

        foreach(var subscription in options.Subscriptions) { // NOSONAR: Explicit loop is clearer and faster than a LINQ pipeline here.
            if(subscription.MessageType is null || subscription.MessageType == typeof(object)) {
                throw new ArgumentException("Every Azure Service Bus subscription must set a concrete MessageType.", nameof(options));
            }
        }

        return options;
    }
}

/// <summary>A dead-letter view of a transport envelope; headers and payload stay inspectable.</summary>
internal sealed class TransportEnvelopeDeferred(TransportEnvelope envelope) : IDeferredEnvelope {
    public int Version => envelope.MessageVersion;

    public string? TenantId => envelope.TenantId;

    public IDeferredEnvelope WithTenant(string? tenantId) => this;

    public TransportEnvelope Envelope { get; } = envelope ?? throw new ArgumentNullException(nameof(envelope));

    public object Command => Envelope;

    public DateTimeOffset DueUtc => Envelope.Timestamp;

    public DateTimeOffset? ExpiresAtUtc => null;

    public Task DispatchAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("A broker dead-letter copy cannot be replayed directly; replay the broker message or the original command.");
}
