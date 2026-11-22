//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>
/// Durable <see cref="IOutboxStore"/> on Entity Framework Core: one row per staged notification,
/// delivered by <see cref="OutboxDispatcher"/> or leased by <see cref="OutboxRelayWorker{TTransport}"/>.
/// Singleton-safe: every operation opens a short-lived context from the factory.
/// </summary>
/// <remarks>
/// Notifications are rebuilt with runtime type resolution, which is why this package is outside
/// the NativeAOT story. Rows whose notification type no longer resolves are removed on listing:
/// they can never be delivered. Payloads above the claim-check threshold live in the
/// claim-check table; inline rows written before claim-check stay readable. Relay leases use
/// conditional <c>UPDATE</c> statements, so two workers on one database cannot both own a row.
/// </remarks>
public sealed class EfOutboxStore(
    IDbContextFactory<IdempotencyDbContext> factory,
    IMessageSerializer serializer,
    ClaimCheckPolicy claimChecks,
    TimeProvider? clock = null,
    ITenantContext? tenant = null,
    IOptions<EfRetentionOptions>? retention = null) : IOutboxStore {
    private readonly IDbContextFactory<IdempotencyDbContext> _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly IMessageSerializer _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    private readonly ClaimCheckPolicy _claimChecks = claimChecks ?? throw new ArgumentNullException(nameof(claimChecks));
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ITenantContext? _tenant = tenant;
    private readonly bool _archiveCompletedOutbox = retention?.Value.ArchiveCompletedOutbox == true;

    private string CurrentTenantId => _tenant?.Current ?? string.Empty;

    /// <inheritdoc />
    public async Task<string> StageAsync(IDeferredEnvelope notification, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(notification);

        var message = notification.Command;
        var key = OutboxKey.New(_clock.GetUtcNow());
        var bytes = _serializer.Serialize(message, message.GetType());
        var claimCheckKey = await _claimChecks.StageIfLargeAsync(bytes, _serializer.ContentType, cancellationToken).ConfigureAwait(false);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.OutboxNotifications.Add(new OutboxRecord {
            TenantId = notification.TenantId ?? CurrentTenantId,
            Key = key,
            NotificationType = message.GetType().AssemblyQualifiedName ?? message.GetType().FullName ?? message.GetType().Name,
            Payload = claimCheckKey is null ? PayloadText.Encode(bytes) : string.Empty,
            ClaimCheckKey = claimCheckKey,
            StagedAtUtc = notification.DueUtc,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return key;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(string Key, IDeferredEnvelope Notification)>> ListAsync(CancellationToken cancellationToken = default) {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var records = await db.OutboxNotifications
            .AsNoTracking()
            .Where(static record => record.CompletedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var listed = new List<(string Key, IDeferredEnvelope Notification)>(records.Count);
        var ghosts = new List<string>();

        foreach(var record in records) {
            var notification = await RebuildAsync(record, cancellationToken).ConfigureAwait(false);

            if(notification is null) {
                ghosts.Add(record.Key);
                continue;
            }

            listed.Add((record.Key, notification));
        }

        if(ghosts.Count > 0) {
            var ghostRecords = records.Where(record => ghosts.Contains(record.Key, StringComparer.Ordinal)).ToList();
            db.OutboxNotifications.RemoveRange(ghostRecords);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            foreach(var record in ghostRecords) {
                await _claimChecks.DeleteAsync(record.ClaimCheckKey, cancellationToken).ConfigureAwait(false);
            }
        }

        listed.Sort(static (left, right) => {
            var byTime = left.Notification.DueUtc.CompareTo(right.Notification.DueUtc);

            return byTime != 0 ? byTime : string.CompareOrdinal(left.Key, right.Key);
        });

        return listed;
    }

    /// <inheritdoc />
    public async Task CompleteAsync(string key, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var record = await db.OutboxNotifications.FindAsync([key], cancellationToken).ConfigureAwait(false);

        if(record is null || !string.Equals(record.TenantId, CurrentTenantId, StringComparison.Ordinal)) {
            return;
        }

        if(_archiveCompletedOutbox) {
            record.CompletedAtUtc = _clock.GetUtcNow();
            record.LeaseOwner = null;
            record.LeaseExpiresAtUtc = null;
            record.Version++;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return;
        }

        var claimCheckKey = record.ClaimCheckKey;
        db.OutboxNotifications.Remove(record);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _claimChecks.DeleteAsync(claimCheckKey, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxLease>> LeaseAsync( // NOSONAR: Complexity is inherent to this state machine/pipeline and is covered by tests.
        string owner,
        int batchSize,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        if(batchSize < 1) {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be at least 1.");
        }

        if(leaseDuration <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), leaseDuration, "Lease duration must be greater than zero.");
        }

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var leaseExpiresAtUtc = now + leaseDuration;
        var candidates = await db.OutboxNotifications
            .AsNoTracking()
            .Where(static record => record.CompletedAtUtc == null)
            .OrderBy(static record => record.Key)
            .Take(batchSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if(candidates.Count is 0) {
            return [];
        }

        var claimed = new List<string>(candidates.Count);

        foreach(var candidate in candidates) {
            var waiting = candidate.NextAttemptUtc is { } nextAttemptUtc && nextAttemptUtc > now;
            var leased = candidate.LeaseOwner is not null &&
                         candidate.LeaseExpiresAtUtc is { } expiresAtUtc &&
                         expiresAtUtc > now;

            if(waiting || leased) {
                break;
            }

            var version = candidate.Version;
            var updated = await db.OutboxNotifications
                .Where(record => record.TenantId == candidate.TenantId && record.Key == candidate.Key && record.Version == version)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(static record => record.LeaseOwner, owner)
                        .SetProperty(static record => record.LeaseExpiresAtUtc, leaseExpiresAtUtc)
                        .SetProperty(static record => record.Version, version + 1),
                    cancellationToken).ConfigureAwait(false);

            if(updated is not 1) {
                break;
            }

            claimed.Add(candidate.Key);
        }

        if(claimed.Count is 0) {
            return [];
        }

        var records = await db.OutboxNotifications
            .AsNoTracking()
            .Where(record => claimed.Contains(record.Key) && record.LeaseOwner == owner)
            .OrderBy(static record => record.Key)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var leases = new List<OutboxLease>(records.Count);
        var ghosts = new List<OutboxRecord>();

        foreach(var record in records) {
            var notification = await RebuildAsync(record, cancellationToken).ConfigureAwait(false);

            if(notification is null) {
                ghosts.Add(record);
                continue;
            }

            leases.Add(new OutboxLease(record.Key, notification, record.Attempts, record.LeaseExpiresAtUtc ?? leaseExpiresAtUtc));
        }

        if(ghosts.Count > 0) {
            db.OutboxNotifications.RemoveRange(ghosts);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            foreach(var record in ghosts) {
                await _claimChecks.DeleteAsync(record.ClaimCheckKey, cancellationToken).ConfigureAwait(false);
            }
        }

        return leases;
    }

    /// <inheritdoc />
    public async Task<bool> CompleteLeaseAsync(string key, string owner, DateTimeOffset now, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var record = await db.OutboxNotifications
            .AsNoTracking()
            .Where(record => record.TenantId == CurrentTenantId && record.Key == key && record.LeaseOwner == owner)
            .Select(static record => new { record.Version, record.LeaseExpiresAtUtc, record.ClaimCheckKey })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if(record is null || record.LeaseExpiresAtUtc is not { } expiresAtUtc || expiresAtUtc <= now) {
            return false;
        }

        var version = record.Version;

        if(_archiveCompletedOutbox) {
            var completed = await db.OutboxNotifications
                .Where(record => record.TenantId == CurrentTenantId && record.Key == key && record.LeaseOwner == owner && record.Version == version)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(static record => record.CompletedAtUtc, now)
                        .SetProperty(static record => record.LeaseOwner, (string?)null)
                        .SetProperty(static record => record.LeaseExpiresAtUtc, (DateTimeOffset?)null)
                        .SetProperty(static record => record.Version, version + 1),
                    cancellationToken).ConfigureAwait(false);

            return completed is 1;
        }

        var deleted = await db.OutboxNotifications
            .Where(record => record.TenantId == CurrentTenantId && record.Key == key && record.LeaseOwner == owner && record.Version == version)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        if(deleted is 0) {
            return false;
        }

        await _claimChecks.DeleteAsync(record.ClaimCheckKey, cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> FailLeaseAsync(
        string key,
        string owner,
        Exception error,
        int attempts,
        DateTimeOffset nextAttemptUtc,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(error);

        if(attempts < 1) {
            throw new ArgumentOutOfRangeException(nameof(attempts), attempts, "Attempts must be at least 1.");
        }

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var record = await db.OutboxNotifications
            .AsNoTracking()
            .Where(record => record.TenantId == CurrentTenantId && record.Key == key && record.LeaseOwner == owner)
            .Select(static record => new { record.Version, record.LeaseExpiresAtUtc })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if(record is null || record.LeaseExpiresAtUtc is not { } expiresAtUtc || expiresAtUtc <= now) {
            return false;
        }

        var errorType = error.GetType().FullName ?? error.GetType().Name;
        var version = record.Version;
        var updated = await db.OutboxNotifications
            .Where(record => record.TenantId == CurrentTenantId && record.Key == key && record.LeaseOwner == owner && record.Version == version)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(static record => record.LeaseOwner, (string?)null)
                    .SetProperty(static record => record.LeaseExpiresAtUtc, (DateTimeOffset?)null)
                    .SetProperty(static record => record.Attempts, attempts)
                    .SetProperty(static record => record.NextAttemptUtc, nextAttemptUtc)
                    .SetProperty(static record => record.LastErrorType, errorType)
                    .SetProperty(static record => record.LastErrorMessage, error.Message)
                    .SetProperty(static record => record.Version, version + 1),
                cancellationToken).ConfigureAwait(false);

        return updated > 0;
    }

    /// <inheritdoc />
    public async Task<bool> ReleaseLeaseAsync(string key, string owner, DateTimeOffset now, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var record = await db.OutboxNotifications
            .AsNoTracking()
            .Where(record => record.TenantId == CurrentTenantId && record.Key == key && record.LeaseOwner == owner)
            .Select(static record => new { record.Version, record.LeaseExpiresAtUtc })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if(record is null || record.LeaseExpiresAtUtc is not { } expiresAtUtc || expiresAtUtc <= now) {
            return false;
        }

        var version = record.Version;
        var updated = await db.OutboxNotifications
            .Where(record => record.TenantId == CurrentTenantId && record.Key == key && record.LeaseOwner == owner && record.Version == version)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(static record => record.LeaseOwner, (string?)null)
                    .SetProperty(static record => record.LeaseExpiresAtUtc, (DateTimeOffset?)null)
                    .SetProperty(static record => record.Version, version + 1),
                cancellationToken).ConfigureAwait(false);

        return updated > 0;
    }

    private async Task<IDeferredEnvelope?> RebuildAsync(OutboxRecord record, CancellationToken cancellationToken) {
        var claimed = await _claimChecks.GetClaimAsync(record.ClaimCheckKey, _serializer.ContentType, cancellationToken).ConfigureAwait(false);
        var bytes = claimed ?? PayloadText.Decode(record.Payload);

        return OutboxEnvelopeSerializer.Rebuild(record.NotificationType, bytes, record.StagedAtUtc, _serializer, record.TenantId);
    }
}