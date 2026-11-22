//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Aws;

/// <summary>Receives one SQS queue, renews visibility while the handler runs, and deletes on success.</summary>
internal sealed class SqsQueueConsumer( // NOSONAR: Public options/records intentionally group related settings; splitting would fragment the API.
    AmazonSQSClient client,
    string queueUrl,
    IMessageSerializer serializer,
    ClaimCheckPolicy claimChecks,
    AwsTransportOptions options,
    TransportTopology topology,
    IMessageTransportHandler handler,
    ReceiveFilterPipeline? receivePipeline = null) {
    private readonly AmazonSQSClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly string _queueUrl = queueUrl ?? throw new ArgumentNullException(nameof(queueUrl));
    private readonly IMessageSerializer _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    private readonly ClaimCheckPolicy _claimChecks = claimChecks ?? throw new ArgumentNullException(nameof(claimChecks));
    private readonly AwsTransportOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly TransportTopology _topology = topology ?? throw new ArgumentNullException(nameof(topology));
    private readonly IMessageTransportHandler _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    private readonly ReceiveFilterPipeline? _receivePipeline = receivePipeline;

    public async Task RunAsync(CancellationToken cancellationToken) {
        while(!cancellationToken.IsCancellationRequested) {
            ReceiveMessageResponse response;

            try {
                response = await _client.ReceiveMessageAsync(new ReceiveMessageRequest {
                    QueueUrl = _queueUrl,
                    MaxNumberOfMessages = _options.MaxNumberOfMessages,
                    WaitTimeSeconds = _options.ReceiveWaitSeconds,
                    VisibilityTimeout = (int)_options.VisibilityTimeout.TotalSeconds,
                }, cancellationToken).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
                return;
            }
            catch(AmazonSQSException) {
                await DelayAsync(cancellationToken).ConfigureAwait(false);

                continue;
            }

            if(response.Messages is null) {
                continue;
            }

            foreach(var message in response.Messages) {
                if(cancellationToken.IsCancellationRequested) {
                    return;
                }

                await ProcessAsync(message, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ProcessAsync(Message message, CancellationToken cancellationToken) {
        if(message.Body is null || message.ReceiptHandle is null) {
            return;
        }

        TransportEnvelope envelope;

        try {
            envelope = await AwsMessageCodec.DecodeAsync(
                message.Body,
                _topology,
                ReadTimestamp(message),
                _claimChecks,
                _serializer,
                cancellationToken).ConfigureAwait(false);
        }
        catch {
            // Leave the message for redelivery; the body is corrupt or its claim is missing.
            return;
        }

        using var renewal = new CancellationTokenSource();
        var renewalTask = RenewVisibilityAsync(message.ReceiptHandle, renewal.Token);

        try {
            if(_receivePipeline is null) {
                await _handler.HandleAsync(envelope, cancellationToken).ConfigureAwait(false);
            } else {
                await _receivePipeline.HandleAsync(_handler, envelope, cancellationToken).ConfigureAwait(false);
            }

            await _client.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, cancellationToken).ConfigureAwait(false);
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch {
            // Do not delete: visibility expires and SQS redelivers (at-least-once).
        }
        finally {
            renewal.Cancel(); // NOSONAR: The synchronous API is intentional for netstandard2.0/net462 compatibility.

            try {
                await renewalTask.ConfigureAwait(false);
            }
            catch(OperationCanceledException) {
                // Expected when the handler completed before the next renewal tick.
            }
        }
    }

    private async Task RenewVisibilityAsync(string receiptHandle, CancellationToken cancellationToken) {
        while(!cancellationToken.IsCancellationRequested) {
            await Task.Delay(_options.VisibilityRenewalInterval, cancellationToken).ConfigureAwait(false);

            try {
                await _client.ChangeMessageVisibilityAsync(new ChangeMessageVisibilityRequest {
                    QueueUrl = _queueUrl,
                    ReceiptHandle = receiptHandle,
                    VisibilityTimeout = (int)_options.VisibilityTimeout.TotalSeconds,
                }, cancellationToken).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
                return;
            }
            catch(AmazonSQSException) {
                // Keep trying until the handler finishes; a lost lease causes at-least-once redelivery.
            }
        }
    }

    private static DateTimeOffset ReadTimestamp(Message message) {
        if(message.Attributes is not null &&
           message.Attributes.TryGetValue("SentTimestamp", out var value) &&
           long.TryParse(value, out var milliseconds)) {
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }

        return DateTimeOffset.UtcNow;
    }

    private static async Task DelayAsync(CancellationToken cancellationToken) {
        try {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
        catch(OperationCanceledException) {
            // The caller observes cancellation on its next iteration.
        }
    }
}
