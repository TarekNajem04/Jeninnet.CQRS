//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>
/// Durable <see cref="IJobStore"/> on Entity Framework Core. Every operation opens a short-lived
/// context; the version token guards lease and status transitions.
/// </summary>
/// <param name="factory">Creates contexts. Never <see langword="null"/>.</param>
/// <param name="clock">Stamps progress and lease updates; defaults to the system clock.</param>
public sealed class EfJobStore(
    IDbContextFactory<IdempotencyDbContext> factory,
    TimeProvider? clock = null) : IJobStore {
    private const int _maxLeaseAttempts = 5;

    private readonly IDbContextFactory<IdempotencyDbContext> _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <inheritdoc />
    public async Task<JobRecord> CreateAsync(JobRecord record, CancellationToken cancellationToken = default) {
        Validate(record);

        var now = _clock.GetUtcNow();
        var entity = ToEntity(record);
        entity.Version = 1;
        entity.UpdatedAtUtc = now;
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Jobs.Add(entity);

        try {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch(DbUpdateException exception) {
            throw new InvalidOperationException($"A job with id '{record.JobId}' already exists.", exception);
        }

        return ToRecord(entity);
    }

    /// <inheritdoc />
    public async Task<JobRecord?> GetAsync(string jobId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.Jobs
            .AsNoTracking()
            .FirstOrDefaultAsync(record => record.JobId == jobId, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : ToRecord(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobRecord>> LeaseDueAsync(
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

        for(var attempt = 0; attempt < _maxLeaseAttempts; attempt++) {
            await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var candidates = await db.Jobs
                .Where(static record => record.Status == (int)JobStatus.Queued || record.Status == (int)JobStatus.Running)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var due = candidates
                .Where(record =>
                    (record.Status == (int)JobStatus.Queued &&
                     !record.CancellationRequested &&
                     record.NextAttemptUtc <= now) ||
                    (record.Status == (int)JobStatus.Running &&
                     record.LeaseExpiresAtUtc is { } expiresAtUtc &&
                     expiresAtUtc <= now))
                .OrderBy(static record => record.NextAttemptUtc)
                .ThenBy(static record => record.CreatedAtUtc)
                .Take(batchSize)
                .ToList();

            if(due.Count is 0) {
                return [];
            }

            foreach(var entity in due) {
                entity.Status = (int)JobStatus.Running;
                entity.Attempts++;
                entity.StartedAtUtc ??= now;
                entity.LeaseOwner = owner;
                entity.LeaseExpiresAtUtc = now + leaseDuration;
                entity.UpdatedAtUtc = now;
                entity.Version++;
            }

            try {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return due.Select(ToRecord).ToList();
            }
            catch(DbUpdateConcurrencyException) {
                // Another worker leased one of the rows; retry the whole pass.
            }
        }

        return [];
    }

    /// <inheritdoc />
    public async Task<bool> ReportProgressAsync(string jobId, string owner, int progress, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.Jobs.FirstOrDefaultAsync(record => record.JobId == jobId, cancellationToken).ConfigureAwait(false);

        if(entity is null ||
           entity.Status != (int)JobStatus.Running ||
           !string.Equals(entity.LeaseOwner, owner, StringComparison.Ordinal)) {
            return false;
        }

        entity.Progress = progress < 0 ? 0 : progress > 100 ? 100 : progress;
        entity.UpdatedAtUtc = _clock.GetUtcNow();
        entity.Version++;

        try {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch(DbUpdateConcurrencyException) {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RenewLeaseAsync(string jobId, string owner, TimeSpan leaseDuration, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        if(leaseDuration <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), leaseDuration, "Lease duration must be greater than zero.");
        }

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.Jobs.FirstOrDefaultAsync(record => record.JobId == jobId, cancellationToken).ConfigureAwait(false);

        if(entity is null ||
           entity.Status != (int)JobStatus.Running ||
           !string.Equals(entity.LeaseOwner, owner, StringComparison.Ordinal)) {
            return false;
        }

        var now = _clock.GetUtcNow();
        entity.LeaseExpiresAtUtc = now + leaseDuration;
        entity.UpdatedAtUtc = now;
        entity.Version++;

        try {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch(DbUpdateConcurrencyException) {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RequestCancellationAsync(string jobId, DateTimeOffset now, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        for(var attempt = 0; attempt < _maxLeaseAttempts; attempt++) {
            await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var entity = await db.Jobs.FirstOrDefaultAsync(record => record.JobId == jobId, cancellationToken).ConfigureAwait(false);

            if(entity is null) {
                return false;
            }

            if(entity.Status is (int)JobStatus.Succeeded or (int)JobStatus.Failed or (int)JobStatus.Canceled) {
                return true;
            }

            entity.CancellationRequested = true;
            entity.UpdatedAtUtc = now;
            entity.Version++;

            if(entity.Status == (int)JobStatus.Queued) {
                entity.Status = (int)JobStatus.Canceled;
                entity.CompletedAtUtc = now;
            }

            try {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return true;
            }
            catch(DbUpdateConcurrencyException) {
                // Another writer changed the row; reload and retry.
            }
        }

        return false;
    }

    /// <inheritdoc />
    public async Task<bool> IsCancellationRequestedAsync(string jobId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.Jobs
            .AsNoTracking()
            .Where(record => record.JobId == jobId)
            .Select(static record => new { record.CancellationRequested })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return entity?.CancellationRequested is true;
    }

    /// <inheritdoc />
    public async Task<bool> CompleteAsync( // NOSONAR: Complexity is inherent to this state machine/pipeline and is covered by tests.
        string jobId,
        string owner,
        JobStatus status,
        int progress,
        Exception? error,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        if(status is not (JobStatus.Succeeded or JobStatus.Failed or JobStatus.Canceled)) {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Only terminal job statuses can be completed.");
        }

        for(var attempt = 0; attempt < _maxLeaseAttempts; attempt++) {
            await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var entity = await db.Jobs.FirstOrDefaultAsync(record => record.JobId == jobId, cancellationToken).ConfigureAwait(false);

            if(entity is null ||
               entity.Status is (int)JobStatus.Succeeded or (int)JobStatus.Failed or (int)JobStatus.Canceled) {
                return false;
            }

            if(entity.Status != (int)JobStatus.Running ||
               !string.Equals(entity.LeaseOwner, owner, StringComparison.Ordinal)) {
                return false;
            }

            entity.Status = (int)status;
            entity.Progress = progress < 0 ? 0 : progress > 100 ? 100 : progress;
            entity.LastErrorType = error is null ? null : ErrorTypeOf(error);
            entity.LastErrorMessage = error?.Message;
            entity.CompletedAtUtc = now;
            entity.UpdatedAtUtc = now;
            entity.LeaseOwner = null;
            entity.LeaseExpiresAtUtc = null;
            entity.Version++;

            try {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return true;
            }
            catch(DbUpdateConcurrencyException) {
                // Reload and retry; ownership is re-checked.
            }
        }

        return false;
    }

    /// <inheritdoc />
    public async Task<bool> RetryAsync(
        string jobId,
        string owner,
        Exception error,
        DateTimeOffset nextAttemptUtc,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(error);

        for(var attempt = 0; attempt < _maxLeaseAttempts; attempt++) {
            await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var entity = await db.Jobs.FirstOrDefaultAsync(record => record.JobId == jobId, cancellationToken).ConfigureAwait(false);

            if(entity is null ||
               entity.Status != (int)JobStatus.Running ||
               !string.Equals(entity.LeaseOwner, owner, StringComparison.Ordinal)) {
                return false;
            }

            entity.Status = (int)JobStatus.Queued;
            entity.NextAttemptUtc = nextAttemptUtc;
            entity.LastErrorType = ErrorTypeOf(error);
            entity.LastErrorMessage = error.Message;
            entity.LeaseOwner = null;
            entity.LeaseExpiresAtUtc = null;
            entity.UpdatedAtUtc = _clock.GetUtcNow();
            entity.Version++;

            try {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return true;
            }
            catch(DbUpdateConcurrencyException) {
                // Reload and retry.
            }
        }

        return false;
    }

    /// <inheritdoc />
    public async Task<int> PruneAsync(DateTimeOffset completedBeforeUtc, CancellationToken cancellationToken = default) {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var candidates = await db.Jobs
            .Where(static record =>
                record.Status == (int)JobStatus.Succeeded ||
                record.Status == (int)JobStatus.Failed ||
                record.Status == (int)JobStatus.Canceled)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var due = candidates
            .Where(record => record.CompletedAtUtc is { } completedAtUtc && completedAtUtc <= completedBeforeUtc)
            .ToList();

        if(due.Count is 0) {
            return 0;
        }

        db.Jobs.RemoveRange(due);

        try {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch(DbUpdateConcurrencyException) {
            return 0;
        }

        return due.Count;
    }

    /// <inheritdoc />
    public async Task<long> CountAsync(JobStatus status, CancellationToken cancellationToken = default) {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.Jobs
            .AsNoTracking()
            .LongCountAsync(record => record.Status == (int)status, cancellationToken)
            .ConfigureAwait(false);
    }

    private static JobRecordEntity ToEntity(JobRecord record) =>
        new() {
            JobId = record.JobId,
            JobType = record.JobType,
            Payload = [.. record.Payload],
            TenantId = record.TenantId ?? string.Empty,
            Status = (int)record.Status,
            Progress = record.Progress,
            Attempts = record.Attempts,
            MaxAttempts = record.MaxAttempts,
            CreatedAtUtc = record.CreatedAtUtc,
            UpdatedAtUtc = record.UpdatedAtUtc,
            NextAttemptUtc = record.NextAttemptUtc,
            StartedAtUtc = record.StartedAtUtc,
            CompletedAtUtc = record.CompletedAtUtc,
            LeaseOwner = record.LeaseOwner,
            LeaseExpiresAtUtc = record.LeaseExpiresAtUtc,
            CancellationRequested = record.CancellationRequested,
            LastErrorType = record.LastErrorType,
            LastErrorMessage = record.LastErrorMessage,
            Version = record.Version,
        };

    private static JobRecord ToRecord(JobRecordEntity entity) =>
        new() {
            JobId = entity.JobId,
            JobType = entity.JobType,
            Payload = [.. entity.Payload],
            TenantId = string.IsNullOrEmpty(entity.TenantId) ? null : entity.TenantId,
            Status = (JobStatus)entity.Status,
            Progress = entity.Progress,
            Attempts = entity.Attempts,
            MaxAttempts = entity.MaxAttempts,
            CreatedAtUtc = entity.CreatedAtUtc,
            UpdatedAtUtc = entity.UpdatedAtUtc,
            NextAttemptUtc = entity.NextAttemptUtc,
            StartedAtUtc = entity.StartedAtUtc,
            CompletedAtUtc = entity.CompletedAtUtc,
            LeaseOwner = entity.LeaseOwner,
            LeaseExpiresAtUtc = entity.LeaseExpiresAtUtc,
            CancellationRequested = entity.CancellationRequested,
            LastErrorType = entity.LastErrorType,
            LastErrorMessage = entity.LastErrorMessage,
            Version = entity.Version,
        };

    private static string ErrorTypeOf(Exception error) =>
        error.GetType().FullName ?? error.GetType().Name;

    private static void Validate(JobRecord record) {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.JobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.JobType);
    }
}