//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>
/// The <see cref="DbContext"/> backing the EF idempotency, deferred-delivery, outbox, scheduler,
/// request-inbox, consumer-inbox, and background-job stores. One database, one migration story for
/// all of them.
/// Register it with <c>AddDbContextFactory&lt;IdempotencyDbContext&gt;</c> (or let
/// <c>AddCqrsEfIdempotencyStore</c> / <c>AddCqrsEfDeferredDelivery</c> do it) and create
/// the schema with <c>EnsureCreatedAsync</c> or the SQL script in the package README.
/// </summary>
public class IdempotencyDbContext(DbContextOptions<IdempotencyDbContext> options, ITenantContext? tenant = null) : DbContext(options) {
    private readonly string? _tenantId = tenant?.Current;

    /// <summary>Gets the stored idempotent responses.</summary>
    public DbSet<IdempotencyRecord> Records => Set<IdempotencyRecord>();

    /// <summary>Gets the staged deferred commands.</summary>
    public DbSet<DeferredRecord> DeferredCommands => Set<DeferredRecord>();

    /// <summary>Gets the dead-lettered commands.</summary>
    public DbSet<DeadLetterRecord> DeadLetters => Set<DeadLetterRecord>();

    /// <summary>Gets the staged outbox notifications.</summary>
    public DbSet<OutboxRecord> OutboxNotifications => Set<OutboxRecord>();

    /// <summary>Gets the pending request inbox table.</summary>
    public DbSet<RequestInboxRecord> RequestInbox => Set<RequestInboxRecord>();

    /// <summary>Gets the persistent scheduler table.</summary>
    public DbSet<ScheduledMessageRecord> ScheduledMessages => Set<ScheduledMessageRecord>();

    /// <summary>Gets the claim-check payloads referenced by message rows.</summary>
    public DbSet<ClaimCheckRecord> ClaimChecks => Set<ClaimCheckRecord>();

    /// <summary>Gets the processed inbound message ids used for consumer-inbox deduplication.</summary>
    public DbSet<InboxRecord> Inbox => Set<InboxRecord>();

    /// <summary>Gets the durable background jobs.</summary>
    public DbSet<JobRecordEntity> Jobs => Set<JobRecordEntity>();

    /// <summary>Gets the archived consumer-inbox rows.</summary>
    public DbSet<InboxArchiveRecord> InboxArchive => Set<InboxArchiveRecord>();

    /// <summary>Gets the archived dead-letter rows.</summary>
    public DbSet<DeadLetterArchiveRecord> DeadLetterArchive => Set<DeadLetterArchiveRecord>();

    /// <summary>Gets the archived terminal jobs.</summary>
    public DbSet<JobArchiveRecord> JobArchive => Set<JobArchiveRecord>();

    /// <summary>Gets the archived completed outbox rows.</summary>
    public DbSet<OutboxArchiveRecord> OutboxArchive => Set<OutboxArchiveRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<IdempotencyRecord>(static entity => {
            entity.HasKey(static record => new { record.TenantId, record.Key });
            entity.Property(static record => record.TenantId).HasMaxLength(128);
            entity.Property(static record => record.Key).HasMaxLength(256);
            entity.Property(static record => record.ResponseType).HasMaxLength(1024);
            entity.Property(static record => record.ClaimCheckKey).HasMaxLength(256);
            entity.Property(static record => record.LeaseOwner).HasMaxLength(256);
            entity.Property(static record => record.Version).IsConcurrencyToken();
            entity.HasIndex(static record => record.ExpiresAtUtc);
            entity.HasIndex(static record => record.LeaseExpiresAtUtc);
        });

        modelBuilder.Entity<DeferredRecord>(static entity => {
            entity.HasKey(static record => new { record.TenantId, record.Key });
            entity.Property(static record => record.TenantId).HasMaxLength(128);
            entity.Property(static record => record.Key).HasMaxLength(256);
            entity.Property(static record => record.CommandType).HasMaxLength(1024);
            entity.Property(static record => record.ClaimCheckKey).HasMaxLength(256);
            entity.Property(static record => record.RecurringKey).HasMaxLength(256);
            entity.Property(static record => record.CronExpression).HasMaxLength(128);
            entity.Property(static record => record.TimeZoneId).HasMaxLength(128);
            entity.Property(static record => record.Version).IsConcurrencyToken();
            entity.HasIndex(static record => record.DueUtc);
            entity.HasIndex(static record => record.ExpiresAtUtc);
        });

        modelBuilder.Entity<DeadLetterRecord>(static entity => {
            entity.HasKey(static record => record.Key);
            entity.Property(static record => record.TenantId).HasMaxLength(128);
            entity.Property(static record => record.Key).HasMaxLength(256);
            entity.Property(static record => record.CommandType).HasMaxLength(1024);
            entity.Property(static record => record.ClaimCheckKey).HasMaxLength(256);
            entity.Property(static record => record.LastErrorType).HasMaxLength(1024);
            entity.Property(static record => record.LastErrorMessage).HasMaxLength(2048);
            entity.HasIndex(static record => record.LastFailedAtUtc);
        });

        modelBuilder.Entity<OutboxRecord>(static entity => {
            entity.HasKey(static record => record.Key);
            entity.Property(static record => record.TenantId).HasMaxLength(128);
            entity.Property(static record => record.Key).HasMaxLength(256);
            entity.Property(static record => record.NotificationType).HasMaxLength(1024);
            entity.Property(static record => record.ClaimCheckKey).HasMaxLength(256);
            entity.Property(static record => record.LeaseOwner).HasMaxLength(256);
            entity.Property(static record => record.LastErrorType).HasMaxLength(1024);
            entity.Property(static record => record.LastErrorMessage).HasMaxLength(2048);
            entity.Property(static record => record.Version).IsConcurrencyToken();
            entity.HasIndex(static record => record.StagedAtUtc);
            entity.HasIndex(static record => record.LeaseExpiresAtUtc);
            entity.HasIndex(static record => record.NextAttemptUtc);
        });

        modelBuilder.Entity<ClaimCheckRecord>(static entity => {
            entity.HasKey(static record => record.Key);
            entity.Property(static record => record.Key).HasMaxLength(256);
            entity.Property(static record => record.ContentType).HasMaxLength(256);
        });

        modelBuilder.Entity<RequestInboxRecord>(static entity => {
            entity.HasKey(static record => record.RequestId);
            entity.Property(static record => record.TenantId).HasMaxLength(128);
            entity.Property(static record => record.RequestId).HasMaxLength(256);
            entity.Property(static record => record.RequestType).HasMaxLength(1024);
            entity.Property(static record => record.ResponseType).HasMaxLength(1024);
            entity.HasIndex(static record => record.ExpiresAtUtc);
        });

        modelBuilder.Entity<InboxRecord>(static entity => {
            entity.HasKey(static record => new { record.TenantId, record.MessageId });
            entity.Property(static record => record.TenantId).HasMaxLength(128);
            entity.Property(static record => record.MessageId).HasMaxLength(256);
            entity.Property(static record => record.ResultHash).HasMaxLength(128);
            entity.Property(static record => record.Version).IsConcurrencyToken();
            entity.HasIndex(static record => record.ProcessedAtUtc);
            entity.HasIndex(static record => record.ExpiresAtUtc);
        });

        modelBuilder.Entity<JobRecordEntity>(static entity => {
            entity.HasKey(static record => record.JobId);
            entity.Property(static record => record.JobId).HasMaxLength(64);
            entity.Property(static record => record.JobType).HasMaxLength(1024);
            entity.Property(static record => record.Payload).IsRequired();
            entity.Property(static record => record.TenantId).HasMaxLength(128);
            entity.Property(static record => record.LeaseOwner).HasMaxLength(256);
            entity.Property(static record => record.LastErrorType).HasMaxLength(1024);
            entity.Property(static record => record.LastErrorMessage).HasMaxLength(2048);
            entity.Property(static record => record.Version).IsConcurrencyToken();
            entity.HasIndex(static record => record.Status);
            entity.HasIndex(static record => record.NextAttemptUtc);
            entity.HasIndex(static record => record.LeaseExpiresAtUtc);
            entity.HasIndex(static record => record.CompletedAtUtc);
        });

        modelBuilder.Entity<ScheduledMessageRecord>(static entity => {
            entity.HasKey(static record => new { record.TenantId, record.Key });
            entity.Property(static record => record.TenantId).HasMaxLength(128);
            entity.Property(static record => record.Key).HasMaxLength(256);
            entity.Property(static record => record.MessageType).HasMaxLength(1024);
            entity.Property(static record => record.ContentType).HasMaxLength(256);
            entity.Property(static record => record.Payload).IsRequired();
            entity.Property(static record => record.CronExpression).HasMaxLength(128);
            entity.Property(static record => record.TimeZoneId).HasMaxLength(128);
            entity.Property(static record => record.LeaseOwner).HasMaxLength(256);
            entity.Property(static record => record.LastErrorType).HasMaxLength(1024);
            entity.Property(static record => record.LastErrorMessage).HasMaxLength(2048);
            entity.Property(static record => record.Version).IsConcurrencyToken();
            entity.HasIndex(static record => record.NextRunUtc);
            entity.HasIndex(static record => record.NextRunTicks);
            entity.HasIndex(static record => record.LeaseExpiresTicks);
        });

        modelBuilder.Entity<InboxArchiveRecord>(static entity => {
            entity.HasKey(static record => new { record.TenantId, record.MessageId, record.ArchivedAtUtc });
            entity.Property(static record => record.TenantId).HasMaxLength(128);
            entity.Property(static record => record.MessageId).HasMaxLength(256);
            entity.Property(static record => record.ResultHash).HasMaxLength(128);
            entity.HasIndex(static record => record.ArchivedAtUtc);
        });

        modelBuilder.Entity<DeadLetterArchiveRecord>(static entity => {
            entity.HasKey(static record => new { record.Key, record.ArchivedAtUtc });
            entity.Property(static record => record.TenantId).HasMaxLength(128);
            entity.Property(static record => record.Key).HasMaxLength(256);
            entity.Property(static record => record.CommandType).HasMaxLength(1024);
            entity.Property(static record => record.ClaimCheckKey).HasMaxLength(256);
            entity.Property(static record => record.LastErrorType).HasMaxLength(1024);
            entity.Property(static record => record.LastErrorMessage).HasMaxLength(2048);
            entity.HasIndex(static record => record.ArchivedAtUtc);
        });

        modelBuilder.Entity<JobArchiveRecord>(static entity => {
            entity.HasKey(static record => new { record.JobId, record.ArchivedAtUtc });
            entity.Property(static record => record.JobId).HasMaxLength(64);
            entity.Property(static record => record.JobType).HasMaxLength(1024);
            entity.Property(static record => record.TenantId).HasMaxLength(128);
            entity.Property(static record => record.LeaseOwner).HasMaxLength(256);
            entity.Property(static record => record.LastErrorType).HasMaxLength(1024);
            entity.Property(static record => record.LastErrorMessage).HasMaxLength(2048);
            entity.HasIndex(static record => record.ArchivedAtUtc);
        });

        modelBuilder.Entity<OutboxArchiveRecord>(static entity => {
            entity.HasKey(static record => new { record.Key, record.ArchivedAtUtc });
            entity.Property(static record => record.TenantId).HasMaxLength(128);
            entity.Property(static record => record.Key).HasMaxLength(256);
            entity.Property(static record => record.NotificationType).HasMaxLength(1024);
            entity.Property(static record => record.ClaimCheckKey).HasMaxLength(256);
            entity.Property(static record => record.LeaseOwner).HasMaxLength(256);
            entity.Property(static record => record.LastErrorType).HasMaxLength(1024);
            entity.Property(static record => record.LastErrorMessage).HasMaxLength(2048);
            entity.HasIndex(static record => record.ArchivedAtUtc);
        });

        ApplyTenantFilters(modelBuilder);
    }

    private void ApplyTenantFilters(ModelBuilder modelBuilder) {
        Filter<IdempotencyRecord>(modelBuilder);
        Filter<DeferredRecord>(modelBuilder);
        Filter<DeadLetterRecord>(modelBuilder);
        Filter<OutboxRecord>(modelBuilder);
        Filter<RequestInboxRecord>(modelBuilder);
        Filter<InboxRecord>(modelBuilder);
        Filter<JobRecordEntity>(modelBuilder);
        Filter<ScheduledMessageRecord>(modelBuilder);
        Filter<InboxArchiveRecord>(modelBuilder);
        Filter<DeadLetterArchiveRecord>(modelBuilder);
        Filter<JobArchiveRecord>(modelBuilder);
        Filter<OutboxArchiveRecord>(modelBuilder);
    }

    private void Filter<TEntity>(ModelBuilder modelBuilder) where TEntity : class, ITenantOwned =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(entity => _tenantId == null || entity.TenantId == _tenantId);
}