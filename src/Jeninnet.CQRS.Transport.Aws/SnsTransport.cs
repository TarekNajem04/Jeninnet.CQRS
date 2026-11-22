//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Aws;

/// <summary>
/// SNS transport for fan-out publishes: one topic per message type, one raw-delivery SQS queue
/// per handler, FIFO support, claim-check handoff, and visibility renewal in each subscriber loop.
/// </summary>
public sealed class SnsTransport : IMessageTransport, IAsyncDisposable, IDisposable {
    private readonly AwsTransportCore _core;
    private readonly Dictionary<string, string> _topicArns = new(StringComparer.Ordinal);

    private AmazonSQSClient? _sqs;
    private AmazonSimpleNotificationServiceClient? _sns;
    private Task[] _loops = [];

    /// <summary>Initializes the SNS transport.</summary>
    /// <param name="options">AWS region, endpoint, FIFO, visibility, and topology options.</param>
    /// <param name="serializer">The payload serializer.</param>
    /// <param name="handlers">Consumer handlers; each gets a subscriber queue.</param>
    /// <param name="namingConvention">Resolves topic/queue names.</param>
    /// <param name="claimChecks">The claim-check policy for large payloads.</param>
    /// <param name="receivePipeline">Applies endpoint filters, retry, and consumption timeout; <see langword="null"/> invokes handlers directly.</param>
    /// <param name="observers">Send, publish, and consume observers; <see langword="null"/> when observation is not configured.</param>
    public SnsTransport(
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

        AmazonSQSClient? sqs = null;
        AmazonSimpleNotificationServiceClient? sns = null;
        var loops = new List<Task>();

        try {
            sqs = AwsClientFactory.CreateSqs(_core.Options);
            sns = AwsClientFactory.CreateSns(_core.Options);

            foreach(var subscription in _core.Options.Subscriptions) {
                var topicArn = await EnsureTopicAsync(sns, subscription, cancellationToken).ConfigureAwait(false);
                var topicName = TopicName(subscription);

                lock(_core.Sync) {
                    _topicArns[subscription.MessageType.AssemblyQualifiedName ?? subscription.MessageType.FullName ?? subscription.MessageType.Name] = topicArn;
                }

                if(_core.Handlers.Length is 0) {
                    continue;
                }

                for(var index = 0; index < _core.Handlers.Length; index++) {
                    var queueName = SubscriberQueueName(subscription, index);
                    var queueUrl = await EnsureQueueAsync(sqs, queueName, cancellationToken).ConfigureAwait(false);
                    var queueArn = await GetQueueArnAsync(sqs, queueUrl, cancellationToken).ConfigureAwait(false);
                    await EnsureSubscriptionAsync(sns, sqs, topicArn, queueUrl, queueArn, cancellationToken).ConfigureAwait(false);

                    var topology = new TransportTopology(topicName, queueName, topicName, topicName);
                    var consumer = new SqsQueueConsumer(sqs, queueUrl, _core.Serializer, _core.ClaimChecks, _core.Options, topology, _core.Handlers[index], _core.ReceivePipeline);
                    loops.Add(Task.Run(() => consumer.RunAsync(lifetime.Token), CancellationToken.None));
                }
            }

            lock(_core.Sync) {
                if(_core.Stopping) {
                    throw new OperationCanceledException("The SNS transport stopped while connecting.");
                }

                _sqs = sqs;
                _sns = sns;
                _loops = [.. loops];
            }
        }
        catch {
            sqs?.Dispose();
            sns?.Dispose();

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
        AmazonSQSClient? sqs;
        AmazonSimpleNotificationServiceClient? sns;
        Task[] loops;

        lock(_core.Sync) {
            _core.Stopping = true;
            _core.Started = false;
            lifetime = _core.Lifetime;
            sqs = _sqs;
            sns = _sns;
            loops = _loops;
            _core.Lifetime = null;
            _sqs = null;
            _sns = null;
            _loops = [];
            _topicArns.Clear();
        }

        lifetime?.Cancel(); // NOSONAR: The synchronous API is intentional for netstandard2.0/net462 compatibility.

        try {
            if(loops.Length > 0) {
                try {
                    await Task.WhenAll(loops).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                }
                catch {
                    // Disposing the clients below unblocks a stuck receive.
                }
            }
        }
        finally {
            sqs?.Dispose();
            sns?.Dispose();
            lifetime?.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task PublishAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) {
        Guard.NotNull(message);
        Guard.NotNull(messageType);

        var sns = GetSns();
        var topicArn = ResolveTopicArn(messageType);
        var envelope = CreateEnvelope(message, messageType, options, TransportDeliveryMode.Publish);
        var body = await AwsMessageCodec.EncodeAsync(envelope, envelope.Payload.ToArray(), _core.ClaimChecks, _core.Options.PayloadLimitBytes, cancellationToken).ConfigureAwait(false);
        var request = new PublishRequest {
            TopicArn = topicArn,
            Message = body,
        };
        if(_core.Options.UseFifo) {
            request.MessageGroupId = envelope.PartitionKey ?? envelope.MessageId;
            request.MessageDeduplicationId = envelope.MessageId;
        }

        await TransportOperation.PublishAsync(
            _core.Observers,
            message,
            messageType,
            envelope,
            ct => sns.PublishAsync(request, ct),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task SendAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("SnsTransport is pub/sub; use SqsTransport or AwsTransport for sends.");

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

    private async Task<string> EnsureTopicAsync(AmazonSimpleNotificationServiceClient sns, AwsSubscription subscription, CancellationToken cancellationToken) {
        var topicName = TopicName(subscription);

        if(!_core.Options.EnsureTopology) {
            var topic = await sns.FindTopicAsync(topicName).ConfigureAwait(false);

            return topic?.TopicArn
                ?? throw new InvalidOperationException($"SNS topic '{topicName}' does not exist.");
        }

        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);

        if(_core.Options.UseFifo) {
            attributes["FifoTopic"] = "true";
            attributes["ContentBasedDeduplication"] = "false";
        }

        var response = await sns.CreateTopicAsync(new CreateTopicRequest {
            Name = topicName,
            Attributes = attributes,
        }, cancellationToken).ConfigureAwait(false);

        return response.TopicArn;
    }

    private async Task<string> EnsureQueueAsync(AmazonSQSClient sqs, string queueName, CancellationToken cancellationToken) {
        if(!_core.Options.EnsureTopology) {
            var existing = await sqs.GetQueueUrlAsync(queueName, cancellationToken).ConfigureAwait(false);

            return existing.QueueUrl;
        }

        var attributes = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["VisibilityTimeout"] = ((int)_core.Options.VisibilityTimeout.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        if(_core.Options.UseFifo) {
            attributes["FifoQueue"] = "true";
            attributes["ContentBasedDeduplication"] = "false";
        }

        var response = await sqs.CreateQueueAsync(new CreateQueueRequest {
            QueueName = queueName,
            Attributes = attributes,
        }, cancellationToken).ConfigureAwait(false);

        return response.QueueUrl;
    }

    private static async Task<string> GetQueueArnAsync(AmazonSQSClient sqs, string queueUrl, CancellationToken cancellationToken) {
        var attributes = await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest {
            QueueUrl = queueUrl,
            AttributeNames = ["QueueArn"],
        }, cancellationToken).ConfigureAwait(false);

        return attributes.Attributes.TryGetValue("QueueArn", out var arn)
            ? arn
            : throw new InvalidOperationException($"SQS queue '{queueUrl}' has no ARN.");
    }

    private async Task EnsureSubscriptionAsync(
        AmazonSimpleNotificationServiceClient sns,
        AmazonSQSClient sqs,
        string topicArn,
        string queueUrl,
        string queueArn,
        CancellationToken cancellationToken) {
        if(!_core.Options.EnsureTopology) {
            return;
        }

        var subscriptions = await sns.ListSubscriptionsByTopicAsync(topicArn, cancellationToken).ConfigureAwait(false);

        if(subscriptions.Subscriptions?.Any(subscription => string.Equals(subscription.Endpoint, queueArn, StringComparison.Ordinal)) is not true) {
            await sns.SubscribeAsync(new SubscribeRequest {
                TopicArn = topicArn,
                Protocol = "sqs",
                Endpoint = queueArn,
                Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["RawMessageDelivery"] = "true" },
            }, cancellationToken).ConfigureAwait(false);
        }

        await SetQueuePolicyAsync(sqs, queueUrl, queueArn, topicArn, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SetQueuePolicyAsync(AmazonSQSClient sqs, string queueUrl, string queueArn, string topicArn, CancellationToken cancellationToken) {
        var policy = JsonSerializer.Serialize(new Dictionary<string, object>(StringComparer.Ordinal) {
            ["Version"] = "2012-10-17",
            ["Statement"] = new[] {
                new Dictionary<string, object>(StringComparer.Ordinal) {
                    ["Effect"] = "Allow",
                    ["Principal"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Service"] = "sns.amazonaws.com" },
                    ["Action"] = "sqs:SendMessage",
                    ["Resource"] = queueArn,
                    ["Condition"] = new Dictionary<string, object>(StringComparer.Ordinal) {
                        ["ArnEquals"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["aws:SourceArn"] = topicArn },
                    },
                },
            },
        });

        await sqs.SetQueueAttributesAsync(queueUrl, new Dictionary<string, string>(StringComparer.Ordinal) { ["Policy"] = policy }, cancellationToken).ConfigureAwait(false);
    }

    private string ResolveTopicArn(Type messageType) {
        var key = messageType.AssemblyQualifiedName ?? messageType.FullName ?? messageType.Name;

        lock(_core.Sync) {
            return _topicArns.TryGetValue(key, out var arn)
                ? arn
                : throw new InvalidOperationException($"No SNS topic is configured for '{messageType.FullName}'. Add it to AwsTransportOptions.Subscriptions.");
        }
    }

    private string TopicName(AwsSubscription subscription) {
        var name = StringCompat.IsNullOrWhiteSpace(subscription.TopicName)
            ? _core.NamingConvention.Describe(subscription.MessageType).Topic
            : subscription.TopicName;

        return _core.Options.UseFifo
            ? $"{AwsMessageCodec.Fit(name, 251)}.fifo"
            : AwsMessageCodec.Fit(name, 256);
    }

    private string SubscriberQueueName(AwsSubscription subscription, int handlerIndex) {
        var baseName = StringCompat.IsNullOrWhiteSpace(subscription.QueueName)
            ? _core.NamingConvention.Describe(subscription.MessageType).Queue
            : subscription.QueueName;
        var name = $"{baseName}.sub.{handlerIndex}";

        return _core.Options.UseFifo
            ? $"{AwsMessageCodec.Fit(name, 75)}.fifo"
            : AwsMessageCodec.Fit(name);
    }

    private TransportEnvelope CreateEnvelope(object message, Type messageType, TransportMessageOptions? options, TransportDeliveryMode deliveryMode) =>
        _core.Envelopes.Create(message, messageType, options, deliveryMode);

    private AmazonSimpleNotificationServiceClient GetSns() {
        if(!_core.Started || _core.Stopping) {
            throw new InvalidOperationException("The SNS transport is not started. Call StartAsync before publishing.");
        }

        return _sns ?? throw new InvalidOperationException("The SNS transport client is not available.");
    }
}
