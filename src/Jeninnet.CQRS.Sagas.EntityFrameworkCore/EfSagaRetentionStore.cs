//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.EntityFrameworkCore;

/// <summary>Retention adapter for terminal saga rows.</summary>
/// <param name="factory">Creates short-lived saga contexts; never <see langword="null"/>.</param>
/// <param name="machines">Registered machines used to decide which states are terminal.</param>
public sealed class EfSagaRetentionStore(
    IDbContextFactory<SagaDbContext> factory,
    IEnumerable<ISaga> machines) : IRetentionStore {
    private readonly IDbContextFactory<SagaDbContext> _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly Dictionary<string, ISaga> _machines = (machines ?? throw new ArgumentNullException(nameof(machines)))
        .GroupBy(static machine => TypeName(machine.StateType), StringComparer.Ordinal)
        .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

    /// <inheritdoc />
    public string Name => RetentionStoreNames.Sagas;

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
        var rows = await db.Sagas.ToListAsync(cancellationToken).ConfigureAwait(false);
        var due = rows
            .Where(record => record.UpdatedAtUtc < cutoff &&
                             _machines.TryGetValue(record.StateType, out var machine) &&
                             machine.IsTerminal(record.CurrentState))
            .OrderBy(static record => record.UpdatedAtUtc)
            .Take(batchSize)
            .ToList();

        if(dryRun || due.Count is 0) {
            return Result(due.Count, policy, dryRun);
        }

        if(policy.Action is RetentionAction.Archive) {
            db.SagaArchive.AddRange(due.Select(record => new SagaArchiveRecordEntity {
                StateType = record.StateType,
                SagaId = record.SagaId,
                State = record.State,
                CurrentState = record.CurrentState,
                Progress = record.Progress,
                Version = record.Version,
                CreatedAtUtc = record.CreatedAtUtc,
                UpdatedAtUtc = record.UpdatedAtUtc,
                ArchivedAtUtc = now,
            }));
        }

        db.Sagas.RemoveRange(due);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result(due.Count, policy, false);
    }

    private RetentionBatchResult Result(int count, RetentionPolicy policy, bool dryRun) =>
        new(Name, count, policy.Action is RetentionAction.Archive ? count : 0, policy.Action is RetentionAction.Delete ? count : 0, dryRun);

    private static string TypeName(Type stateType) =>
        stateType.AssemblyQualifiedName ?? stateType.FullName ?? stateType.Name;
}
