//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Consumer-side seam for a transport. The transport only delivers the canonical envelope; the
/// handler (for example a relay worker) owns deserialization and dispatch.
/// </summary>
public interface IMessageTransportHandler {
    /// <summary>Handles one delivered envelope.</summary>
    /// <param name="envelope">The delivered envelope.</param>
    /// <param name="cancellationToken">Cancels the handling.</param>
    Task HandleAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default);
}
