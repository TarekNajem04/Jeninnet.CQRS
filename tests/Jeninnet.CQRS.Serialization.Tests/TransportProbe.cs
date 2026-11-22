//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Serialization.Tests;

/// <summary>Captures delivered envelopes for in-memory transport assertions.</summary>
internal sealed class TransportProbe : IMessageTransportHandler {
    private readonly Lock _sync = new();

    /// <summary>Gets the envelopes received so far, in delivery order.</summary>
    public List<TransportEnvelope> Received { get; } = [];

    /// <inheritdoc />
    public Task HandleAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default) {
        lock(_sync) {
            Received.Add(envelope);
        }

        return Task.CompletedTask;
    }
}