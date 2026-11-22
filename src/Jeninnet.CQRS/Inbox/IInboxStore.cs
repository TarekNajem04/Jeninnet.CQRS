//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Inbox;

/// <summary>
/// Durable deduplication table for inbound transport messages. The inbox filter records a message
/// id before running the handler, so an at-least-once broker redelivery cannot repeat the side
/// effects. The in-memory implementation is the default; the EF Core package replaces it.
/// </summary>
/// <remarks>
/// <see cref="TryBeginAsync"/> must be atomic per message id: exactly one concurrent delivery may
/// receive <see langword="true"/>. A processed row is permanent until pruned; a pending row is
/// reclaimable once <see cref="InboxMessage.ExpiresAtUtc"/> passes, which is the documented
/// at-least-once window after a crash between the handler and completion.
/// </remarks>
public interface IInboxStore {
    /// <summary>
    /// Atomically claims a message id for execution.
    /// </summary>
    /// <param name="message">The inbound message.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// <see langword="true"/> when the caller owns the delivery and must run the handler;
    /// <see langword="false"/> when the id is already processed or another consumer holds an
    /// unexpired pending row.
    /// </returns>
    Task<bool> TryBeginAsync(InboxMessage message, CancellationToken cancellationToken = default);

    /// <summary>Marks a claimed message as processed and records its optional payload hash.</summary>
    /// <param name="messageId">The broker message id that was claimed.</param>
    /// <param name="tenantId">The tenant partition, or <see langword="null"/> for the global partition.</param>
    /// <param name="resultHash">The payload hash to retain for audits; may be <see langword="null"/>.</param>
    /// <param name="processedAtUtc">When the handler completed.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task CompleteAsync(
        string messageId,
        string? tenantId,
        string? resultHash,
        DateTimeOffset processedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Releases a pending claim after a handler failure so a retry can run immediately.</summary>
    /// <param name="messageId">The broker message id that was claimed.</param>
    /// <param name="tenantId">The tenant partition, or <see langword="null"/> for the global partition.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    Task AbandonAsync(string messageId, string? tenantId, CancellationToken cancellationToken = default);

    /// <summary>Reads one inbox row for tests and operational tooling.</summary>
    /// <param name="messageId">The broker message id.</param>
    /// <param name="tenantId">The tenant partition, or <see langword="null"/> for the global partition.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The row, or <see langword="null"/> when the id was never seen.</returns>
    Task<InboxEntry?> GetAsync(string messageId, string? tenantId = null, CancellationToken cancellationToken = default);

    /// <summary>Gets whether a message id is already processed.</summary>
    /// <param name="messageId">The broker message id.</param>
    /// <param name="tenantId">The tenant partition, or <see langword="null"/> for the global partition.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<bool> IsProcessedAsync(string messageId, string? tenantId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes processed rows older than <paramref name="processedBeforeUtc"/> and pending rows
    /// older than <paramref name="pendingBeforeUtc"/>.
    /// </summary>
    /// <param name="processedBeforeUtc">Retention cutoff for completed rows.</param>
    /// <param name="pendingBeforeUtc">Cutoff for abandoned pending rows.</param>
    /// <param name="cancellationToken">Cancels the cleanup.</param>
    /// <returns>The number of rows removed.</returns>
    Task<int> PruneAsync(
        DateTimeOffset processedBeforeUtc,
        DateTimeOffset pendingBeforeUtc,
        CancellationToken cancellationToken = default);
}
