//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.EntityFrameworkCore;

/// <summary>
/// Durable <see cref="ISagaStore"/> on Entity Framework Core. Every operation opens a short-lived
/// context; the version token is the concurrency guard, mirroring the idempotency store.
/// </summary>
/// <param name="factory">Creates contexts. Never <see langword="null"/>.</param>
/// <param name="serializer">Serializes the state payload. Never <see langword="null"/>.</param>
/// <param name="clock">Stamps creation/update times; defaults to the system clock.</param>
public sealed class EfSagaStore(
    IDbContextFactory<SagaDbContext> factory,
    IMessageSerializer serializer,
    TimeProvider? clock = null) : ISagaStore {
    private readonly IDbContextFactory<SagaDbContext> _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly IMessageSerializer _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <inheritdoc />
    public async Task<SagaRecord?> LoadAsync(string sagaId, Type stateType, CancellationToken cancellationToken = default) {
        ValidateKey(sagaId, stateType);

        var stateTypeName = TypeName(stateType);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.Sagas
            .AsNoTracking()
            .FirstOrDefaultAsync(record => record.SagaId == sagaId && record.StateType == stateTypeName, cancellationToken)
            .ConfigureAwait(false);

        if(entity is null) {
            return null;
        }

        var state = _serializer.Deserialize(entity.State, stateType) as ISagaState
            ?? throw new InvalidOperationException($"The stored saga state for '{stateType.FullName}' did not deserialize to {nameof(ISagaState)}.");

        return new SagaRecord(sagaId, stateType, state, entity.Version, ReadProgress(entity.Progress));
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        string sagaId,
        Type stateType,
        ISagaState state,
        long expectedVersion,
        SagaProgress progress,
        CancellationToken cancellationToken = default) {
        ValidateKey(sagaId, stateType);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(progress);

        if(expectedVersion < 0) {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion), expectedVersion, "Expected version must not be negative.");
        }

        var stateTypeName = TypeName(stateType);
        var payload = _serializer.Serialize(state, stateType);
        var progressJson = WriteProgress(progress);
        var now = _clock.GetUtcNow();
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        if(expectedVersion is 0) {
            db.Sagas.Add(new SagaRecordEntity {
                SagaId = sagaId,
                StateType = stateTypeName,
                State = payload,
                CurrentState = state.CurrentState,
                Progress = progressJson,
                Version = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });

            try {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch(DbUpdateException exception) {
                throw new SagaConcurrencyException(sagaId, stateType, expectedVersion, -1, exception);
            }

            return;
        }

        var existing = await db.Sagas
            .FirstOrDefaultAsync(record => record.SagaId == sagaId && record.StateType == stateTypeName, cancellationToken)
            .ConfigureAwait(false);

        if(existing is null) {
            throw new SagaConcurrencyException(sagaId, stateType, expectedVersion, -1);
        }

        if(existing.Version != expectedVersion) {
            throw new SagaConcurrencyException(sagaId, stateType, expectedVersion, existing.Version);
        }

        existing.State = payload;
        existing.CurrentState = state.CurrentState;
        existing.Progress = progressJson;
        existing.Version = expectedVersion + 1;
        existing.UpdatedAtUtc = now;

        try {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch(DbUpdateConcurrencyException exception) {
            throw new SagaConcurrencyException(sagaId, stateType, expectedVersion, -1, exception);
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string sagaId, Type stateType, CancellationToken cancellationToken = default) {
        ValidateKey(sagaId, stateType);

        var stateTypeName = TypeName(stateType);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.Sagas
            .Where(record => record.SagaId == sagaId && record.StateType == stateTypeName)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> CountAsync(Type stateType, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(stateType);

        var stateTypeName = TypeName(stateType);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.Sagas
            .AsNoTracking()
            .LongCountAsync(record => record.StateType == stateTypeName, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SagaSnapshot>> ListAsync(Type stateType, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(stateType);

        var stateTypeName = TypeName(stateType);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Sagas
            .AsNoTracking()
            .Where(record => record.StateType == stateTypeName)
            .Select(static record => new { record.SagaId, record.CurrentState, record.UpdatedAtUtc })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(row => new SagaSnapshot(row.SagaId, stateType, row.CurrentState, row.UpdatedAtUtc))
            .ToList();
    }

    private static string TypeName(Type stateType) =>
        stateType.AssemblyQualifiedName ?? stateType.FullName ?? stateType.Name;

    private static SagaProgress ReadProgress(string? json) {
        if(string.IsNullOrWhiteSpace(json)) {
            return SagaProgress.Empty;
        }

        var dto = JsonSerializer.Deserialize<ProgressDto>(json)
            ?? throw new InvalidOperationException("The stored saga progress is empty or corrupt.");

        return new SagaProgress(
            dto.CompletedSteps ?? [],
            new HashSet<string>(dto.CompensatedSteps ?? [], StringComparer.Ordinal));
    }

    private static string WriteProgress(SagaProgress progress) =>
        JsonSerializer.Serialize(new ProgressDto(
            [.. progress.CompletedSteps],
            [.. progress.CompensatedSteps.Order(StringComparer.Ordinal)]));

    private sealed record ProgressDto(string[]? CompletedSteps, string[]? CompensatedSteps);

    private static void ValidateKey(string sagaId, Type stateType) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sagaId);
        ArgumentNullException.ThrowIfNull(stateType);
    }
}
