//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>Retention adapter for completed consumer-inbox rows.</summary>
/// <param name="factory">Creates short-lived contexts; never <see langword="null"/>.</param>
public sealed class EfInboxRetentionStore(IDbContextFactory<IdempotencyDbContext> factory) : IRetentionStore {
    private readonly IDbContextFactory<IdempotencyDbContext> _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <inheritdoc />
    public string Name => RetentionStoreNames.Inbox;

    /// <inheritdoc />
    public async Task<RetentionBatchResult> SweepAsync(
        RetentionPolicy policy,
        DateTimeOffset now,
        int batchSize,
        bool dryRun,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(policy);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var cutoff = now - policy.Retention;
        var rows = await db.Inbox.IgnoreQueryFilters().ToListAsync(cancellationToken).ConfigureAwait(false);
        var due = rows
            .Where(record => record.ProcessedAtUtc is { } processedAtUtc && processedAtUtc < cutoff)
            .OrderBy(static record => record.ProcessedAtUtc)
            .Take(batchSize)
            .ToList();

        if(dryRun || due.Count is 0) {
            return Result(due.Count, policy, dryRun);
        }

        if(policy.Action is RetentionAction.Archive) {
            db.InboxArchive.AddRange(due.Select(record => new InboxArchiveRecord {
                TenantId = record.TenantId,
                MessageId = record.MessageId,
                ReceivedAtUtc = record.ReceivedAtUtc,
                ProcessedAtUtc = record.ProcessedAtUtc,
                ResultHash = record.ResultHash,
                ExpiresAtUtc = record.ExpiresAtUtc,
                Version = record.Version,
                ArchivedAtUtc = now,
            }));
        }

        db.Inbox.RemoveRange(due);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result(due.Count, policy, false);
    }

    private RetentionBatchResult Result(int count, RetentionPolicy policy, bool dryRun) =>
        new(Name, count, policy.Action is RetentionAction.Archive ? count : 0, policy.Action is RetentionAction.Delete ? count : 0, dryRun);
}

/// <summary>Retention adapter for dead-letter rows after the policy retention and grace period.</summary>
/// <param name="factory">Creates short-lived contexts; never <see langword="null"/>.</param>
public sealed class EfDeadLetterRetentionStore(IDbContextFactory<IdempotencyDbContext> factory) : IRetentionStore {
    private readonly IDbContextFactory<IdempotencyDbContext> _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <inheritdoc />
    public string Name => RetentionStoreNames.DeadLetters;

    /// <inheritdoc />
    public async Task<RetentionBatchResult> SweepAsync(
        RetentionPolicy policy,
        DateTimeOffset now,
        int batchSize,
        bool dryRun,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(policy);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var cutoff = now - policy.Retention - policy.GracePeriod;
        var rows = await db.DeadLetters.IgnoreQueryFilters().ToListAsync(cancellationToken).ConfigureAwait(false);
        var due = rows
            .Where(record => record.LastFailedAtUtc < cutoff)
            .OrderBy(static record => record.LastFailedAtUtc)
            .Take(batchSize)
            .ToList();

        if(dryRun || due.Count is 0) {
            return Result(due.Count, policy, dryRun);
        }

        if(policy.Action is RetentionAction.Archive) {
            db.DeadLetterArchive.AddRange(due.Select(record => new DeadLetterArchiveRecord {
                TenantId = record.TenantId,
                Key = record.Key,
                CommandType = record.CommandType,
                Payload = record.Payload,
                ClaimCheckKey = record.ClaimCheckKey,
                DueUtc = record.DueUtc,
                ExpiresAtUtc = record.ExpiresAtUtc,
                Attempts = record.Attempts,
                LastErrorType = record.LastErrorType,
                LastErrorMessage = record.LastErrorMessage,
                FirstFailedAtUtc = record.FirstFailedAtUtc,
                LastFailedAtUtc = record.LastFailedAtUtc,
                ArchivedAtUtc = now,
            }));
        }

        db.DeadLetters.RemoveRange(due);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result(due.Count, policy, false);
    }

    private RetentionBatchResult Result(int count, RetentionPolicy policy, bool dryRun) =>
        new(Name, count, policy.Action is RetentionAction.Archive ? count : 0, policy.Action is RetentionAction.Delete ? count : 0, dryRun);
}

/// <summary>Retention adapter for terminal background jobs.</summary>
/// <param name="factory">Creates short-lived contexts; never <see langword="null"/>.</param>
public sealed class EfJobRetentionStore(IDbContextFactory<IdempotencyDbContext> factory) : IRetentionStore {
    private readonly IDbContextFactory<IdempotencyDbContext> _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <inheritdoc />
    public string Name => RetentionStoreNames.Jobs;

    /// <inheritdoc />
    public async Task<RetentionBatchResult> SweepAsync(
        RetentionPolicy policy,
        DateTimeOffset now,
        int batchSize,
        bool dryRun,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(policy);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var cutoff = now - policy.Retention;
        var rows = await db.Jobs.IgnoreQueryFilters().ToListAsync(cancellationToken).ConfigureAwait(false);
        var due = rows
            .Where(record => IsTerminal(record.Status) && record.CompletedAtUtc is { } completedAtUtc && completedAtUtc < cutoff)
            .OrderBy(static record => record.CompletedAtUtc)
            .Take(batchSize)
            .ToList();

        if(dryRun || due.Count is 0) {
            return Result(due.Count, policy, dryRun);
        }

        if(policy.Action is RetentionAction.Archive) {
            db.JobArchive.AddRange(due.Select(record => new JobArchiveRecord {
                JobId = record.JobId,
                JobType = record.JobType,
                Payload = record.Payload,
                TenantId = record.TenantId,
                Status = record.Status,
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
                ArchivedAtUtc = now,
            }));
        }

        db.Jobs.RemoveRange(due);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result(due.Count, policy, false);
    }

    private static bool IsTerminal(int status) =>
        status is (int)JobStatus.Succeeded or (int)JobStatus.Failed or (int)JobStatus.Canceled;

    private RetentionBatchResult Result(int count, RetentionPolicy policy, bool dryRun) =>
        new(Name, count, policy.Action is RetentionAction.Archive ? count : 0, policy.Action is RetentionAction.Delete ? count : 0, dryRun);
}

/// <summary>Retention adapter for completed outbox rows.</summary>
/// <param name="factory">Creates short-lived contexts; never <see langword="null"/>.</param>
/// <param name="claimChecks">Optional claim-check cleanup for delete mode.</param>
public sealed class EfOutboxRetentionStore(
    IDbContextFactory<IdempotencyDbContext> factory,
    ClaimCheckPolicy? claimChecks = null) : IRetentionStore {
    private readonly IDbContextFactory<IdempotencyDbContext> _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly ClaimCheckPolicy? _claimChecks = claimChecks;

    /// <inheritdoc />
    public string Name => RetentionStoreNames.Outbox;

    /// <inheritdoc />
    public async Task<RetentionBatchResult> SweepAsync(
        RetentionPolicy policy,
        DateTimeOffset now,
        int batchSize,
        bool dryRun,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(policy);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var cutoff = now - policy.Retention;
        var rows = await db.OutboxNotifications.IgnoreQueryFilters().ToListAsync(cancellationToken).ConfigureAwait(false);
        var due = rows
            .Where(record => record.CompletedAtUtc is { } completedAtUtc && completedAtUtc < cutoff)
            .OrderBy(static record => record.CompletedAtUtc)
            .Take(batchSize)
            .ToList();

        if(dryRun || due.Count is 0) {
            return Result(due.Count, policy, dryRun);
        }

        if(policy.Action is RetentionAction.Archive) {
            db.OutboxArchive.AddRange(due.Select(record => new OutboxArchiveRecord {
                TenantId = record.TenantId,
                Key = record.Key,
                NotificationType = record.NotificationType,
                Payload = record.Payload,
                ClaimCheckKey = record.ClaimCheckKey,
                StagedAtUtc = record.StagedAtUtc,
                LeaseOwner = record.LeaseOwner,
                LeaseExpiresAtUtc = record.LeaseExpiresAtUtc,
                Attempts = record.Attempts,
                NextAttemptUtc = record.NextAttemptUtc,
                LastErrorType = record.LastErrorType,
                LastErrorMessage = record.LastErrorMessage,
                Version = record.Version,
                CompletedAtUtc = record.CompletedAtUtc,
                ArchivedAtUtc = now,
            }));
        } else if(_claimChecks is not null) {
            foreach(var record in due) {
                await _claimChecks.DeleteAsync(record.ClaimCheckKey, cancellationToken).ConfigureAwait(false);
            }
        }

        db.OutboxNotifications.RemoveRange(due);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result(due.Count, policy, false);
    }

    private RetentionBatchResult Result(int count, RetentionPolicy policy, bool dryRun) =>
        new(Name, count, policy.Action is RetentionAction.Archive ? count : 0, policy.Action is RetentionAction.Delete ? count : 0, dryRun);
}
