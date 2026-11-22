//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Aws;

/// <summary>
/// SQS transport for point-to-point sends: one queue per message type, visibility renewal while
/// a handler runs, claim-check handoff above the payload limit, and at-least-once deletion after
/// success.
/// </summary>
public sealed class SqsTransport : IMessageTransport, IAsyncDisposable, IDisposable {
    private readonly AwsTransportCore _core;
    private readonly Dictionary<string, string> _queueUrls = new(StringComparer.Ordinal);

    private AmazonSQSClient? _client;
    private Task[] _loops = [];

    /// <summary>Initializes the SQS transport.</summary>
    /// <param name="options">AWS region, endpoint, FIFO, visibility, and topology options.</param>
    /// <param name="serializer">The payload serializer.</param>
    /// <param name="handlers">Consumer handlers; SQS sends are handled by the first.</param>
    /// <param name="namingConvention">Resolves queue names.</param>
    /// <param name="claimChecks">The claim-check policy for large payloads.</param>
    /// <param name="receivePipeline">Applies endpoint filters, retry, and consumption timeout; <see langword="null"/> invokes handlers directly.</param>
    /// <param name="observers">Send, publish, and consume observers; <see langword="null"/> when observation is not configured.</param>
    public SqsTransport(
        IOptions<AwsTransportOptions> options,
        IMessageSerializer serializer,
        IEnumerable<IMessageTransportHandler> handlers,
        TransportNamingConvention namingConvention,
        ClaimCheckPolicy claimChecks,
        ReceiveFilterPipeline? receivePipeline = null,
        MessageObserverInvoker? observers = null) {
        _core = new AwsTransportCore(options, serializer, handlers, namingConvention, claimChecks, receivePipeline, observers);
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default) {
        if(!_core.TryBeginStart(out var lifetime)) {
            return;
        }

        AmazonSQSClient? client = null;
        var loops = new List<Task>();

        try {
            client = AwsClientFactory.CreateSqs(_core.Options);

            foreach(var subscription in _core.Options.Subscriptions) {
                var queueUrl = await EnsureQueueAsync(client, subscription, cancellationToken).ConfigureAwait(false);

                lock(_core.Sync) {
                    _queueUrls[subscription.MessageType.AssemblyQualifiedName ?? subscription.MessageType.FullName ?? subscription.MessageType.Name] = queueUrl;
                }
            }

            if(_core.Handlers.Length > 0) {
                foreach(var subscription in _core.Options.Subscriptions) {
                    var queueUrl = await EnsureQueueAsync(client, subscription, cancellationToken).ConfigureAwait(false);
                    var topology = new TransportTopology(
                        QueueName(subscription),
                        QueueName(subscription),
                        QueueName(subscription),
                        QueueName(subscription));
                    var consumer = new SqsQueueConsumer(client, queueUrl, _core.Serializer, _core.ClaimChecks, _core.Options, topology, _core.Handlers[0], _core.ReceivePipeline);
                    loops.Add(Task.Run(() => consumer.RunAsync(lifetime.Token), CancellationToken.None));
                }
            }

            lock(_core.Sync) {
                if(_core.Stopping) {
                    throw new OperationCanceledException("The SQS transport stopped while connecting.");
                }

                _client = client;
                _loops = [.. loops];
            }
        }
        catch {
            client?.Dispose();

            lock(_core.Sync) {
                _core.Started = false;
                _core.Stopping = true;
                _core.Dispose();
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default) {
        CancellationTokenSource? lifetime;
        AmazonSQSClient? client;
        Task[] loops;

        lock(_core.Sync) {
            _core.Stopping = true;
            _core.Started = false;
            lifetime = _core.Lifetime;
            client = _client;
            loops = _loops;
            _core.Lifetime = null;
            _client = null;
            _loops = [];
            _queueUrls.Clear();
        }

        lifetime?.Cancel(); // NOSONAR: The synchronous API is intentional for netstandard2.0/net462 compatibility.

        try {
            if(loops.Length > 0) {
                try {
                    await Task.WhenAll(loops).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                }
                catch {
                    // Disposing the client below unblocks a stuck receive.
                }
            }
        }
        finally {
            client?.Dispose();
            lifetime?.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task SendAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) {
        Guard.NotNull(message);
        Guard.NotNull(messageType);

        var client = GetClient();
        var queueUrl = ResolveQueueUrl(messageType);
        var envelope = CreateEnvelope(message, messageType, options, TransportDeliveryMode.Send);
        var body = await AwsMessageCodec.EncodeAsync(envelope, envelope.Payload.ToArray(), _core.ClaimChecks, _core.Options.PayloadLimitBytes, cancellationToken).ConfigureAwait(false);
        var request = new SendMessageRequest {
            QueueUrl = queueUrl,
            MessageBody = body,
        };
        if(_core.Options.UseFifo) {
            request.MessageGroupId = envelope.PartitionKey ?? envelope.MessageId;
            request.MessageDeduplicationId = envelope.MessageId;
        }

        await TransportOperation.SendAsync(
            _core.Observers,
            message,
            messageType,
            envelope,
            ct => client.SendMessageAsync(request, ct),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task PublishAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("SqsTransport is point-to-point; use SnsTransport or AwsTransport for publishes.");

    /// <inheritdoc />
    public async ValueTask DisposeAsync() {
        lock(_core.Sync) {
            if(_core.Disposed) {
                return;
            }

            _core.Disposed = true;
        }

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() =>
        DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task<string> EnsureQueueAsync(AmazonSQSClient client, AwsSubscription subscription, CancellationToken cancellationToken) {
        var queueName = QueueName(subscription);

        if(!_core.Options.EnsureTopology) {
            var existing = await client.GetQueueUrlAsync(queueName, cancellationToken).ConfigureAwait(false);

            return existing.QueueUrl;
        }

        var attributes = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["VisibilityTimeout"] = ((int)_core.Options.VisibilityTimeout.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        if(_core.Options.UseFifo) {
            attributes["FifoQueue"] = "true";
            attributes["ContentBasedDeduplication"] = "false";
        }

        var response = await client.CreateQueueAsync(new CreateQueueRequest {
            QueueName = queueName,
            Attributes = attributes,
        }, cancellationToken).ConfigureAwait(false);

        return response.QueueUrl;
    }

    private string ResolveQueueUrl(Type messageType) {
        var key = messageType.AssemblyQualifiedName ?? messageType.FullName ?? messageType.Name;

        lock(_core.Sync) {
            return _queueUrls.TryGetValue(key, out var url)
                ? url
                : throw new InvalidOperationException($"No SQS queue is configured for '{messageType.FullName}'. Add it to AwsTransportOptions.Subscriptions.");
        }
    }

    private string QueueName(AwsSubscription subscription) {
        var name = StringCompat.IsNullOrWhiteSpace(subscription.QueueName)
            ? _core.NamingConvention.Describe(subscription.MessageType).Queue
            : subscription.QueueName;

        return _core.Options.UseFifo
            ? $"{AwsMessageCodec.Fit(name, 75)}.fifo"
            : AwsMessageCodec.Fit(name);
    }

    private TransportEnvelope CreateEnvelope(object message, Type messageType, TransportMessageOptions? options, TransportDeliveryMode deliveryMode) =>
        _core.Envelopes.Create(message, messageType, options, deliveryMode);

    private AmazonSQSClient GetClient() {
        if(!_core.Started || _core.Stopping) {
            throw new InvalidOperationException("The SQS transport is not started. Call StartAsync before sending.");
        }

        return _client ?? throw new InvalidOperationException("The SQS transport client is not available.");
    }
}