//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Consume-side filter that can accept or reject a delivered message before the consumer handler
/// runs. Filters are registered globally for a message type or for one endpoint; both forms run in
/// registration order and see the envelope headers.
/// </summary>
/// <remarks>
/// Return <see langword="false"/> to drop the delivery without invoking the handler. A filter that
/// throws is treated as a handler failure, so the transport can retry, dead-letter, or abandon the
/// message according to its own semantics. Implementations should be stateless and fast; the
/// pipeline runs them once per delivery, outside the retry loop.
/// </remarks>
public interface IReceiveFilter {
    /// <summary>Decides whether the message reaches the consumer handler.</summary>
    /// <param name="context">The decoded message, envelope, and endpoint policy.</param>
    /// <param name="cancellationToken">Cancels the filter.</param>
    /// <returns><see langword="true"/> to continue; <see langword="false"/> to drop the delivery.</returns>
    ValueTask<bool> FilterAsync(ReceiveContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Typed marker for a receive filter that applies to <typeparamref name="TMessage"/>. Register it
/// with <c>AddReceiveFilter&lt;TMessage, TFilter&gt;()</c> or through an endpoint builder.
/// </summary>
/// <typeparam name="TMessage">The message type the filter applies to.</typeparam>
public interface IReceiveFilter<TMessage> : IReceiveFilter {
}