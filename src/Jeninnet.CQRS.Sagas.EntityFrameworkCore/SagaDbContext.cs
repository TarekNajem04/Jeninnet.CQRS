//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.EntityFrameworkCore;

/// <summary>
/// Dedicated saga database context. Keeping it separate from the idempotency context means a saga
/// can be adopted without bringing the other durability tables; its schema is the saga table plus
/// the optional routing-slip table.
/// </summary>
/// <param name="options">The provider options.</param>
public sealed class SagaDbContext(DbContextOptions<SagaDbContext> options) : DbContext(options) {
    /// <summary>Gets the saga table.</summary>
    public DbSet<SagaRecordEntity> Sagas => Set<SagaRecordEntity>();

    /// <summary>Gets the routing-slip table.</summary>
    public DbSet<RoutingSlipEntity> RoutingSlips => Set<RoutingSlipEntity>();

    /// <summary>Gets the archived terminal sagas.</summary>
    public DbSet<SagaArchiveRecordEntity> SagaArchive => Set<SagaArchiveRecordEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        modelBuilder.Entity<SagaRecordEntity>(static entity => {
            entity.HasKey(static record => new { record.StateType, record.SagaId });
            entity.Property(static record => record.StateType).HasMaxLength(1024);
            entity.Property(static record => record.SagaId).HasMaxLength(256);
            entity.Property(static record => record.State).IsRequired();
            entity.Property(static record => record.CurrentState).HasMaxLength(128);
            entity.Property(static record => record.Progress).IsRequired();
            entity.Property(static record => record.Version).IsConcurrencyToken();
            entity.HasIndex(static record => record.UpdatedAtUtc);
        });

        modelBuilder.Entity<SagaArchiveRecordEntity>(static entity => {
            entity.HasKey(static record => new { record.StateType, record.SagaId, record.ArchivedAtUtc });
            entity.Property(static record => record.StateType).HasMaxLength(1024);
            entity.Property(static record => record.SagaId).HasMaxLength(256);
            entity.Property(static record => record.State).IsRequired();
            entity.Property(static record => record.CurrentState).HasMaxLength(128);
            entity.Property(static record => record.Progress).IsRequired();
            entity.HasIndex(static record => record.ArchivedAtUtc);
        });

        modelBuilder.Entity<RoutingSlipEntity>(static entity => {
            entity.HasKey(static record => record.SlipId);
            entity.Property(static record => record.SlipId).HasMaxLength(128);
            entity.Property(static record => record.DefinitionName).HasMaxLength(256);
            entity.Property(static record => record.ArgumentsType).HasMaxLength(1024);
            entity.Property(static record => record.ArgumentsPayload).IsRequired();
            entity.Property(static record => record.TenantId).HasMaxLength(128);
            entity.Property(static record => record.FailedStep).HasMaxLength(256);
            entity.Property(static record => record.FailureErrorType).HasMaxLength(1024);
            entity.Property(static record => record.FailureErrorMessage).HasMaxLength(2048);
            entity.Property(static record => record.CompensationErrorType).HasMaxLength(1024);
            entity.Property(static record => record.CompensationErrorMessage).HasMaxLength(2048);
            entity.Property(static record => record.Version).IsConcurrencyToken();
            entity.HasIndex(static record => record.DefinitionName);
            entity.HasIndex(static record => record.Status);
            entity.HasIndex(static record => record.UpdatedAtUtc);
        });
    }
}