//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Aws;

/// <summary>
/// Shared state for the AWS transports. SNS and SQS have the same dependency set, options
/// validation, envelope policy, and single-flight start semantics; only the broker-specific
/// clients and loops differ.
/// </summary>
internal sealed class AwsTransportCore : IDisposable {
    /// <summary>The validated AWS options.</summary>
    internal AwsTransportOptions Options { get; }

    /// <summary>The payload serializer.</summary>
    internal IMessageSerializer Serializer { get; }

    /// <summary>The registered consumer handlers.</summary>
    internal IMessageTransportHandler[] Handlers { get; }

    /// <summary>The topic/queue naming convention.</summary>
    internal TransportNamingConvention NamingConvention { get; }

    /// <summary>The claim-check policy.</summary>
    internal ClaimCheckPolicy ClaimChecks { get; }

    /// <summary>The optional receive pipeline.</summary>
    internal ReceiveFilterPipeline? ReceivePipeline { get; }

    /// <summary>The optional observer invoker.</summary>
    internal MessageObserverInvoker? Observers { get; }

    /// <summary>The shared envelope builder.</summary>
    internal TransportEnvelopeBuilder Envelopes { get; }

    /// <summary>Guards the lifecycle fields.</summary>
    internal readonly Lock Sync = new();

    /// <summary>The transport lifetime, or <see langword="null"/> when stopped.</summary>
    internal CancellationTokenSource? Lifetime;

    /// <summary>Whether the transport is started.</summary>
    internal volatile bool Started;

    /// <summary>Whether the transport is stopping.</summary>
    internal volatile bool Stopping;

    /// <summary>Whether the transport is disposed.</summary>
    internal bool Disposed;

    /// <summary>Initializes the shared AWS transport state.</summary>
    /// <param name="options">AWS region, endpoint, FIFO, visibility, and topology options.</param>
    /// <param name="serializer">The payload serializer.</param>
    /// <param name="handlers">Consumer handlers.</param>
    /// <param name="namingConvention">Resolves topic/queue names.</param>
    /// <param name="claimChecks">The claim-check policy for large payloads.</param>
    /// <param name="receivePipeline">Applies endpoint filters, retry, and consumption timeout.</param>
    /// <param name="observers">Send, publish, and consume observers.</param>
    public AwsTransportCore(
        IOptions<AwsTransportOptions> options,
        IMessageSerializer serializer,
        IEnumerable<IMessageTransportHandler> handlers,
        TransportNamingConvention namingConvention,
        ClaimCheckPolicy claimChecks,
        ReceiveFilterPipeline? receivePipeline,
        MessageObserverInvoker? observers) {
        Options = AwsOptionsValidator.Validate((options ?? throw new ArgumentNullException(nameof(options))).Value);
        Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        Handlers = handlers?.ToArray() ?? [];
        NamingConvention = namingConvention ?? throw new ArgumentNullException(nameof(namingConvention));
        ClaimChecks = claimChecks ?? throw new ArgumentNullException(nameof(claimChecks));
        ReceivePipeline = receivePipeline;
        Observers = observers;
        Envelopes = TransportEnvelopeBuilders.ForBroker(Serializer, NamingConvention.Describe);
    }

    /// <summary>Disposes the transport lifetime.</summary>
    public void Dispose() {
        Lifetime?.Dispose();
        Lifetime = null;
    }

    /// <summary>
    /// Marks the transport started and creates its lifetime. Returns <see langword="false"/> when
    /// it was already started, so the caller can return without doing any broker work.
    /// </summary>
    /// <param name="lifetime">The transport lifetime when startup should continue.</param>
    /// <returns><see langword="true"/> when startup should continue.</returns>
    internal bool TryBeginStart(out CancellationTokenSource lifetime) {
        lock(Sync) {
            Guard.NotDisposed(Disposed, this);

            if(Started) {
                lifetime = null!;

                return false;
            }

            Started = true;
            Stopping = false;
            Lifetime = new CancellationTokenSource();
            lifetime = Lifetime;

            return true;
        }
    }
}
