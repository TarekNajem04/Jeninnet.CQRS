//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>
/// Durable <see cref="ISchedulerStore"/> on Entity Framework Core. Due scans use the indexed
/// <c>NextRunTicks</c> column (SQLite cannot compare DateTimeOffset in SQL), and claims are
/// version-guarded, so two workers cannot fire the same row.
/// </summary>
/// <param name="factory">Creates short-lived contexts.</param>
/// <param name="clock">Stamps update times; defaults to the system clock.</param>
/// <param name="tenant">The ambient tenant; <see langword="null"/> without tenancy.</param>
public sealed class EfSchedulerStore(IDbContextFactory<IdempotencyDbContext> factory, TimeProvider? clock = null, ITenantContext? tenant = null) : ISchedulerStore {
    private readonly IDbContextFactory<IdempotencyDbContext> _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ITenantContext? _tenant = tenant;

    private string CurrentTenantId => _tenant?.Current ?? string.Empty;

    /// <inheritdoc />
    public async Task<bool> TryAddAsync(ScheduledMessage message, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(message);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ScheduledMessages.Add(new ScheduledMessageRecord {
            TenantId = message.TenantId ?? CurrentTenantId,
            Key = message.Key,
            MessageType = message.MessageType,
            ContentType = message.ContentType,
            Payload = message.Payload,
            Kind = (int)message.Kind,
            NextRunUtc = message.NextRunUtc,
            NextRunTicks = message.NextRunUtc.UtcTicks,
            CreatedAtUtc = message.CreatedAtUtc,
            UpdatedAtUtc = _clock.GetUtcNow(),
            CronExpression = message.CronExpression,
            TimeZoneId = message.TimeZoneId,
            Attempts = message.Attempts,
            Version = 1,
        });

        try {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch(DbUpdateException) {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SchedulerLease>> LeaseDueAsync(
        string owner,
        int batchSize,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        if(batchSize < 1) {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be at least 1.");
        }

        if(leaseDuration <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), leaseDuration, "Lease duration must be greater than zero.");
        }

        var nowTicks = now.UtcTicks;
        var expiresAtUtc = now + leaseDuration;
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var candidates = await db.ScheduledMessages
            .AsNoTracking()
            .Where(record => record.NextRunTicks <= nowTicks &&
                             (record.LeaseOwner == null || record.LeaseExpiresTicks == null || record.LeaseExpiresTicks <= nowTicks))
            .OrderBy(static record => record.NextRunTicks)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if(candidates.Count is 0) {
            return [];
        }

        var leases = new List<SchedulerLease>(candidates.Count);

        foreach(var candidate in candidates) {
            var version = candidate.Version;
            var updated = await db.ScheduledMessages
                .Where(record => record.TenantId == candidate.TenantId &&
                                 record.Key == candidate.Key &&
                                 record.Version == version &&
                                 record.NextRunTicks <= nowTicks &&
                                 (record.LeaseOwner == null || record.LeaseExpiresTicks == null || record.LeaseExpiresTicks <= nowTicks))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(static record => record.LeaseOwner, owner)
                        .SetProperty(static record => record.LeaseExpiresAtUtc, expiresAtUtc)
                        .SetProperty(static record => record.LeaseExpiresTicks, expiresAtUtc.UtcTicks)
                        .SetProperty(static record => record.Version, version + 1),
                    cancellationToken).ConfigureAwait(false);

            if(updated is not 1) {
                continue;
            }

            leases.Add(new SchedulerLease(ToMessage(candidate) with { Version = version + 1 }, owner, expiresAtUtc));
        }

        return leases;
    }

    /// <inheritdoc />
    public async Task<bool> CompleteAsync(string key, string owner, long version, CancellationToken cancellationToken = default) {
        ValidateKeyOwner(key, owner);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var deleted = await db.ScheduledMessages
            .Where(record => record.TenantId == CurrentTenantId && record.Key == key && record.LeaseOwner == owner && record.Version == version)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return deleted > 0;
    }

    /// <inheritdoc />
    public async Task<bool> ScheduleNextAsync(string key, string owner, long version, DateTimeOffset nextRunUtc, CancellationToken cancellationToken = default) {
        ValidateKeyOwner(key, owner);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var updated = await db.ScheduledMessages
            .Where(record => record.TenantId == CurrentTenantId && record.Key == key && record.LeaseOwner == owner && record.Version == version)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(static record => record.NextRunUtc, nextRunUtc)
                    .SetProperty(static record => record.NextRunTicks, nextRunUtc.UtcTicks)
                    .SetProperty(static record => record.LeaseOwner, (string?)null)
                    .SetProperty(static record => record.LeaseExpiresAtUtc, (DateTimeOffset?)null)
                    .SetProperty(static record => record.LeaseExpiresTicks, (long?)null)
                    .SetProperty(static record => record.Attempts, 0)
                    .SetProperty(static record => record.Version, version + 1)
                    .SetProperty(static record => record.UpdatedAtUtc, _clock.GetUtcNow()),
                cancellationToken).ConfigureAwait(false);

        return updated > 0;
    }

    /// <inheritdoc />
    public async Task<bool> FailAsync(
        string key,
        string owner,
        long version,
        Exception error,
        int attempts,
        DateTimeOffset nextRunUtc,
        CancellationToken cancellationToken = default) {
        ValidateKeyOwner(key, owner);
        ArgumentNullException.ThrowIfNull(error);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var updated = await db.ScheduledMessages
            .Where(record => record.TenantId == CurrentTenantId && record.Key == key && record.LeaseOwner == owner && record.Version == version)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(static record => record.NextRunUtc, nextRunUtc)
                    .SetProperty(static record => record.NextRunTicks, nextRunUtc.UtcTicks)
                    .SetProperty(static record => record.Attempts, attempts)
                    .SetProperty(static record => record.LeaseOwner, (string?)null)
                    .SetProperty(static record => record.LeaseExpiresAtUtc, (DateTimeOffset?)null)
                    .SetProperty(static record => record.LeaseExpiresTicks, (long?)null)
                    .SetProperty(static record => record.LastErrorType, error.GetType().FullName ?? error.GetType().Name)
                    .SetProperty(static record => record.LastErrorMessage, error.Message)
                    .SetProperty(static record => record.Version, version + 1)
                    .SetProperty(static record => record.UpdatedAtUtc, _clock.GetUtcNow()),
                cancellationToken).ConfigureAwait(false);

        return updated > 0;
    }

    /// <inheritdoc />
    public async Task<bool> CancelAsync(string key, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var deleted = await db.ScheduledMessages
            .Where(record => record.TenantId == CurrentTenantId && record.Key == key)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return deleted > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScheduledMessage>> ListAsync(CancellationToken cancellationToken = default) {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var records = await db.ScheduledMessages
            .AsNoTracking()
            .OrderBy(static record => record.NextRunTicks)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return records.Select(ToMessage).ToList();
    }

    /// <inheritdoc />
    public async Task<long> CountDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default) {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.ScheduledMessages
            .AsNoTracking()
            .LongCountAsync(record => record.NextRunTicks <= now.UtcTicks, cancellationToken)
            .ConfigureAwait(false);
    }

    private static ScheduledMessage ToMessage(ScheduledMessageRecord record) =>
        new(
            record.Key,
            record.MessageType,
            record.ContentType,
            record.Payload,
            (ScheduledMessageKind)record.Kind,
            record.NextRunUtc,
            record.CreatedAtUtc,
            record.CronExpression,
            record.TimeZoneId,
            record.Attempts,
            record.Version,
            record.TenantId);

    private static void ValidateKeyOwner(string key, string owner) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
    }
}
