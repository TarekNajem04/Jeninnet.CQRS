//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Consumer middleware that wraps the handler for one inbound delivery. Middleware runs after
/// receive filters accept the message and in registration order; each instance decides whether to
/// call <c>next</c> or short-circuit the delivery.
/// </summary>
/// <remarks>
/// Use middleware for cross-cutting work that must observe the handler's completion (inbox
/// deduplication, transaction scopes, ambient context). Filters remain the right tool for a pure
/// accept/reject decision. Implementations are resolved once from DI, so they must be
/// singleton-safe or depend only on singletons.
/// </remarks>
public interface IReceiveMiddleware {
    /// <summary>Invokes the next middleware or the consumer handler.</summary>
    /// <param name="context">The envelope and endpoint policy for this delivery.</param>
    /// <param name="next">The remaining chain; returns <see langword="false"/> when an inner layer dropped the delivery.</param>
    /// <param name="cancellationToken">Cancels the delivery.</param>
    /// <returns><see langword="true"/> when the delivery completed; <see langword="false"/> when it was dropped.</returns>
    Task<bool> InvokeAsync(
        ReceiveMiddlewareContext context,
        Func<CancellationToken, Task<bool>> next,
        CancellationToken cancellationToken = default);
}
