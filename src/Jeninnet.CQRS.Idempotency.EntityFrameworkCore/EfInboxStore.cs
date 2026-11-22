//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>
/// Durable <see cref="IInboxStore"/> on Entity Framework Core. One row per
/// <c>(TenantId, MessageId)</c>; the unique key decides the first concurrent delivery, and the
/// optimistic-concurrency token protects reclaim and completion. Singleton-safe: every operation
/// opens a short-lived context from the factory.
/// </summary>
/// <remarks>
/// A row inserted by a losing writer is observed as a duplicate on re-read. A pending row whose
/// <see cref="InboxRecord.ExpiresAtUtc"/> passed is reclaimed by the next delivery, which is the
/// documented at-least-once window after a crash between handler success and completion.
/// SQLite guidance is the same as the idempotency store: enable WAL and a busy timeout.
/// </remarks>
public sealed class EfInboxStore(
    IDbContextFactory<IdempotencyDbContext> factory,
    IOptions<EfInboxStoreOptions> options,
    TimeProvider? clock = null,
    ITenantContext? tenant = null) : IInboxStore {
    private readonly IDbContextFactory<IdempotencyDbContext> _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly EfInboxStoreOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ITenantContext? _tenant = tenant;

    /// <inheritdoc />
    public async Task<bool> TryBeginAsync(InboxMessage message, CancellationToken cancellationToken = default) { // NOSONAR: Complexity is inherent to this state machine/pipeline and is covered by tests.
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.MessageId);

        var tenantId = ResolveTenant(message.TenantId);

        for(var attempt = 0; attempt <= _options.MaxAttempts; attempt++) {
            var now = _clock.GetUtcNow();
            await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var record = await db.Inbox.FindAsync([tenantId, message.MessageId], cancellationToken).ConfigureAwait(false);

            if(record is null) {
                db.Inbox.Add(new InboxRecord {
                    TenantId = tenantId,
                    MessageId = message.MessageId,
                    ReceivedAtUtc = message.ReceivedAtUtc,
                    ExpiresAtUtc = message.ExpiresAtUtc,
                });

                try {
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    return true;
                }
                catch(DbUpdateException) {
                    // Another consumer inserted the same key first; re-read and observe it.
                    if(attempt < _options.MaxAttempts) {
                        continue;
                    }

                    throw;
                }
            }

            if(record.ProcessedAtUtc is not null) {
                return false;
            }

            if(record.ExpiresAtUtc is { } expiresAtUtc && expiresAtUtc > now) {
                return false;
            }

            record.ReceivedAtUtc = message.ReceivedAtUtc;
            record.ExpiresAtUtc = message.ExpiresAtUtc;
            record.ResultHash = null;
            record.Version++;

            try {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return true;
            }
            catch(DbUpdateConcurrencyException) {
                // Another consumer reclaimed or completed the row; re-read.
            }
        }

        throw new InvalidOperationException(
            $"Could not claim inbox message '{message.MessageId}' after {_options.MaxAttempts} retries.");
    }

    /// <inheritdoc />
    public async Task CompleteAsync(
        string messageId,
        string? tenantId,
        string? resultHash,
        DateTimeOffset processedAtUtc,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        var resolvedTenant = ResolveTenant(tenantId);

        for(var attempt = 0; attempt <= _options.MaxAttempts; attempt++) {
            await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var record = await db.Inbox.FindAsync([resolvedTenant, messageId], cancellationToken).ConfigureAwait(false);

            if(record is null || record.ProcessedAtUtc is not null) {
                return;
            }

            record.ProcessedAtUtc = processedAtUtc;
            record.ResultHash = resultHash;
            record.ExpiresAtUtc = null;
            record.Version++;

            try {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return;
            }
            catch(DbUpdateConcurrencyException) {
                // Re-read: another consumer may have reclaimed the pending row.
            }
        }

        throw new InvalidOperationException(
            $"Could not complete inbox message '{messageId}' after {_options.MaxAttempts} retries.");
    }

    /// <inheritdoc />
    public async Task AbandonAsync(string messageId, string? tenantId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        var resolvedTenant = ResolveTenant(tenantId);

        for(var attempt = 0; attempt <= _options.MaxAttempts; attempt++) {
            await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var record = await db.Inbox.FindAsync([resolvedTenant, messageId], cancellationToken).ConfigureAwait(false);

            if(record is null || record.ProcessedAtUtc is not null) {
                return;
            }

            db.Inbox.Remove(record);

            try {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return;
            }
            catch(DbUpdateConcurrencyException) {
                // Another consumer completed or reclaimed the row; leave it alone.
            }
        }
    }

    /// <inheritdoc />
    public async Task<InboxEntry?> GetAsync(string messageId, string? tenantId = null, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var record = await db.Inbox.FindAsync([ResolveTenant(tenantId), messageId], cancellationToken).ConfigureAwait(false);

        return record is null
            ? null
            : new InboxEntry(
                record.MessageId,
                record.TenantId,
                record.ReceivedAtUtc,
                record.ProcessedAtUtc,
                record.ResultHash,
                record.ExpiresAtUtc);
    }

    /// <inheritdoc />
    public async Task<bool> IsProcessedAsync(string messageId, string? tenantId = null, CancellationToken cancellationToken = default) {
        var entry = await GetAsync(messageId, tenantId, cancellationToken).ConfigureAwait(false);

        return entry?.IsProcessed is true;
    }

    /// <inheritdoc />
    public async Task<int> PruneAsync(
        DateTimeOffset processedBeforeUtc,
        DateTimeOffset pendingBeforeUtc,
        CancellationToken cancellationToken = default) {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // SQLite cannot translate DateTimeOffset comparisons, so the cut happens in memory.
        var candidates = await db.Inbox
            .Where(static record => record.ProcessedAtUtc != null || record.ExpiresAtUtc != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var due = candidates
            .Where(record =>
                record.ProcessedAtUtc is { } processedAtUtc
                    ? processedAtUtc <= processedBeforeUtc
                    : record.ExpiresAtUtc is { } expiresAtUtc && expiresAtUtc <= pendingBeforeUtc)
            .ToList();

        if(due.Count is 0) {
            return 0;
        }

        db.Inbox.RemoveRange(due);

        try {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch(DbUpdateConcurrencyException) {
            return 0; // Another pruner won the same rows.
        }

        return due.Count;
    }

    private string ResolveTenant(string? tenantId) =>
        _tenant?.Current ?? tenantId ?? string.Empty;
}