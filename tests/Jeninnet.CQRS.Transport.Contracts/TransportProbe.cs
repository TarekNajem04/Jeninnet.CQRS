//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Contracts;

/// <summary>Records every envelope a transport delivers.</summary>
public sealed class TransportProbe : IMessageTransportHandler {
    private readonly Lock _sync = new();
    private readonly List<TransportEnvelope> _received = [];

    /// <summary>Gets a snapshot of the received envelopes.</summary>
    public IReadOnlyList<TransportEnvelope> Received {
        get {
            lock(_sync) {
                return [.. _received];
            }
        }
    }

    /// <inheritdoc />
    public Task HandleAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(envelope);

        lock(_sync) {
            _received.Add(envelope);
        }

        return Task.CompletedTask;
    }
}

/// <summary>A canonical message used by every transport contract suite.</summary>
/// <param name="Name">A string payload.</param>
/// <param name="Value">A numeric payload.</param>
public sealed record TransportContractMessage(string Name, int Value);
