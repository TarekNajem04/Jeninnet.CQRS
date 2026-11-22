//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Aws;

/// <summary>
/// Facade for the AWS transport: sends through SQS queues and publishes through SNS topics,
/// sharing the same options, serializer, naming convention, and claim-check policy.
/// </summary>
public sealed class AwsTransport : IMessageTransport, IAsyncDisposable, IDisposable {
    private readonly SqsTransport _sqs;
    private readonly SnsTransport _sns;
    private bool _disposed;

    /// <summary>Initializes both AWS transport legs.</summary>
    /// <param name="options">AWS region, endpoint, FIFO, visibility, and topology options.</param>
    /// <param name="serializer">The payload serializer.</param>
    /// <param name="handlers">Consumer handlers; SQS sends use the first, SNS publishes reach all.</param>
    /// <param name="namingConvention">Resolves queue/topic names.</param>
    /// <param name="claimChecks">The claim-check policy for large payloads.</param>
    /// <param name="receivePipeline">Applies endpoint filters, retry, and consumption timeout to both legs; <see langword="null"/> invokes handlers directly.</param>
    /// <param name="observers">Send, publish, and consume observers; <see langword="null"/> when observation is not configured.</param>
    public AwsTransport(
        IOptions<AwsTransportOptions> options,
        IMessageSerializer serializer,
        IEnumerable<IMessageTransportHandler> handlers,
        TransportNamingConvention namingConvention,
        ClaimCheckPolicy claimChecks,
        ReceiveFilterPipeline? receivePipeline = null,
        MessageObserverInvoker? observers = null) {
        Guard.NotNull(options);

        _sqs = new SqsTransport(options, serializer, handlers, namingConvention, claimChecks, receivePipeline, observers);
        _sns = new SnsTransport(options, serializer, handlers, namingConvention, claimChecks, receivePipeline, observers);
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default) {
        await _sqs.StartAsync(cancellationToken).ConfigureAwait(false);

        try {
            await _sns.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch {
            await _sqs.StopAsync(CancellationToken.None).ConfigureAwait(false);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default) {
        await _sns.StopAsync(cancellationToken).ConfigureAwait(false);
        await _sqs.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task SendAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) =>
        _sqs.SendAsync(message, messageType, options, cancellationToken);

    /// <inheritdoc />
    public Task PublishAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) =>
        _sns.PublishAsync(message, messageType, options, cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync() {
        if(_disposed) {
            return;
        }

        _disposed = true;
        await _sns.DisposeAsync().ConfigureAwait(false);
        await _sqs.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() =>
        DisposeAsync().AsTask().GetAwaiter().GetResult();
}
