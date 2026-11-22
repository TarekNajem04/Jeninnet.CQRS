//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.EntityFrameworkCore;

/// <summary>
/// Durable <see cref="IRoutingSlipStore"/> on Entity Framework Core. One row per slip; the version
/// token guards progress writes, and argument/progress columns are written after every step so a
/// restart resumes at the next operation.
/// </summary>
public sealed class EfRoutingSlipStore(
    IDbContextFactory<SagaDbContext> factory,
    TimeProvider? clock = null) : IRoutingSlipStore {
    private readonly IDbContextFactory<SagaDbContext> _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <inheritdoc />
    public async Task<RoutingSlipRecord> CreateAsync(RoutingSlipRecord record, CancellationToken cancellationToken = default) {
        Validate(record);

        var entity = ToEntity(record);
        entity.Version = 1;
        entity.UpdatedAtUtc = _clock.GetUtcNow();
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.RoutingSlips.Add(entity);

        try {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch(DbUpdateException exception) {
            throw new RoutingSlipConcurrencyException(record.SlipId, 0, 1, exception);
        }

        return ToRecord(entity);
    }

    /// <inheritdoc />
    public async Task<RoutingSlipRecord?> LoadAsync(string slipId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(slipId);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.RoutingSlips
            .AsNoTracking()
            .FirstOrDefaultAsync(record => record.SlipId == slipId, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : ToRecord(entity);
    }

    /// <inheritdoc />
    public async Task<RoutingSlipRecord> SaveAsync(RoutingSlipRecord record, long expectedVersion, CancellationToken cancellationToken = default) {
        Validate(record);

        if(expectedVersion < 0) {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion), expectedVersion, "Expected version must not be negative.");
        }

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.RoutingSlips
            .FirstOrDefaultAsync(row => row.SlipId == record.SlipId, cancellationToken)
            .ConfigureAwait(false);

        if(entity is null) {
            throw new RoutingSlipConcurrencyException(record.SlipId, expectedVersion, -1);
        }

        if(entity.Version != expectedVersion) {
            throw new RoutingSlipConcurrencyException(record.SlipId, expectedVersion, entity.Version);
        }

        entity.DefinitionName = record.DefinitionName;
        entity.ArgumentsType = record.ArgumentsType;
        entity.ArgumentsPayload = [.. record.ArgumentsPayload];
        entity.Status = (int)record.Status;
        entity.NextStepIndex = record.NextStepIndex;
        entity.CompletedSteps = WriteSteps(record.CompletedSteps);
        entity.CompensatedSteps = WriteSteps(record.CompensatedSteps);
        entity.FailedStep = record.FailedStep;
        entity.FailureErrorType = record.FailureErrorType;
        entity.FailureErrorMessage = record.FailureErrorMessage;
        entity.CompensationErrorType = record.CompensationErrorType;
        entity.CompensationErrorMessage = record.CompensationErrorMessage;
        entity.TenantId = record.TenantId;
        entity.Version = expectedVersion + 1;
        entity.UpdatedAtUtc = record.UpdatedAtUtc;

        try {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch(DbUpdateConcurrencyException exception) {
            throw new RoutingSlipConcurrencyException(record.SlipId, expectedVersion, -1, exception);
        }

        return ToRecord(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoutingSlipSnapshot>> ListAsync(string? definitionName = null, CancellationToken cancellationToken = default) {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query = db.RoutingSlips.AsNoTracking();

        if(!string.IsNullOrWhiteSpace(definitionName)) {
            query = query.Where(record => record.DefinitionName == definitionName);
        }

        var rows = await query.ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows
            .OrderByDescending(static row => row.UpdatedAtUtc)
            .ThenBy(static row => row.SlipId, StringComparer.Ordinal)
            .Select(static row => new RoutingSlipSnapshot(
                row.SlipId,
                row.DefinitionName,
                (RoutingSlipStatus)row.Status,
                row.NextStepIndex,
                ReadSteps(row.CompletedSteps),
                ReadSteps(row.CompensatedSteps),
                row.FailedStep,
                row.CompensationErrorMessage ?? row.FailureErrorMessage,
                row.UpdatedAtUtc))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<long> CountAsync(RoutingSlipStatus status, CancellationToken cancellationToken = default) {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.RoutingSlips
            .AsNoTracking()
            .LongCountAsync(record => record.Status == (int)status, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string slipId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(slipId);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.RoutingSlips
            .Where(record => record.SlipId == slipId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static RoutingSlipEntity ToEntity(RoutingSlipRecord record) =>
        new() {
            SlipId = record.SlipId,
            DefinitionName = record.DefinitionName,
            ArgumentsType = record.ArgumentsType,
            ArgumentsPayload = [.. record.ArgumentsPayload],
            Status = (int)record.Status,
            NextStepIndex = record.NextStepIndex,
            CompletedSteps = WriteSteps(record.CompletedSteps),
            CompensatedSteps = WriteSteps(record.CompensatedSteps),
            FailedStep = record.FailedStep,
            FailureErrorType = record.FailureErrorType,
            FailureErrorMessage = record.FailureErrorMessage,
            CompensationErrorType = record.CompensationErrorType,
            CompensationErrorMessage = record.CompensationErrorMessage,
            TenantId = record.TenantId,
            Version = record.Version,
            CreatedAtUtc = record.CreatedAtUtc,
            UpdatedAtUtc = record.UpdatedAtUtc,
        };

    private static RoutingSlipRecord ToRecord(RoutingSlipEntity entity) =>
        new() {
            SlipId = entity.SlipId,
            DefinitionName = entity.DefinitionName,
            ArgumentsType = entity.ArgumentsType,
            ArgumentsPayload = [.. entity.ArgumentsPayload],
            Status = (RoutingSlipStatus)entity.Status,
            NextStepIndex = entity.NextStepIndex,
            CompletedSteps = ReadSteps(entity.CompletedSteps),
            CompensatedSteps = ReadSteps(entity.CompensatedSteps),
            FailedStep = entity.FailedStep,
            FailureErrorType = entity.FailureErrorType,
            FailureErrorMessage = entity.FailureErrorMessage,
            CompensationErrorType = entity.CompensationErrorType,
            CompensationErrorMessage = entity.CompensationErrorMessage,
            TenantId = entity.TenantId,
            CreatedAtUtc = entity.CreatedAtUtc,
            UpdatedAtUtc = entity.UpdatedAtUtc,
            Version = entity.Version,
        };

    private static string WriteSteps(IReadOnlyList<string> steps) =>
        JsonSerializer.Serialize(steps);

    private static string[] ReadSteps(string? json) {
        if(string.IsNullOrWhiteSpace(json)) {
            return [];
        }

        try {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch(JsonException exception) {
            throw new InvalidOperationException("The stored routing-slip step list is corrupt.", exception);
        }
    }

    private static void Validate(RoutingSlipRecord record) {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.SlipId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.DefinitionName);
    }
}
