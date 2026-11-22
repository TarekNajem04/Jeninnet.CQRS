//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas;

/// <summary>
/// Strongly typed saga persistence seam. The in-memory and EF Core stores are both available
/// through <see cref="SagaRepository{TState}"/>, so a saga machine can be tested or hosted with a
/// replaceable repository.
/// </summary>
/// <typeparam name="TState">The concrete saga state type.</typeparam>
public interface ISagaRepository<TState> where TState : class, ISagaState {
    /// <summary>Loads one saga, or returns <see langword="null"/> when the id is unknown.</summary>
    /// <param name="sagaId">The correlation id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<SagaRepositoryEntry<TState>?> LoadAsync(string sagaId, CancellationToken cancellationToken = default);

    /// <summary>Saves the state only when <paramref name="expectedVersion"/> still matches.</summary>
    /// <param name="sagaId">The correlation id.</param>
    /// <param name="state">The mutated state.</param>
    /// <param name="expectedVersion">The version read before the transition.</param>
    /// <param name="progress">Compensation progress to persist with the state.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="SagaConcurrencyException">Another writer saved first.</exception>
    Task SaveAsync(
        string sagaId,
        TState state,
        long expectedVersion,
        SagaProgress progress,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a saga row; a missing row is a no-op.</summary>
    /// <param name="sagaId">The correlation id.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    Task DeleteAsync(string sagaId, CancellationToken cancellationToken = default);
}

/// <summary>One loaded saga state and its optimistic-concurrency version.</summary>
/// <param name="SagaId">The correlation id.</param>
/// <param name="State">The typed state.</param>
/// <param name="Version">The persisted version.</param>
/// <param name="Progress">Compensation progress.</param>
public sealed record SagaRepositoryEntry<TState>(string SagaId, TState State, long Version, SagaProgress Progress);

/// <summary>
/// Adapts any <see cref="ISagaStore"/> to a strongly typed repository. The store decides whether
/// rows are process-local (<see cref="InMemorySagaStore"/>) or durable (<c>EfSagaStore</c>); the
/// version and concurrency contract is identical.
/// </summary>
/// <typeparam name="TState">The concrete saga state type.</typeparam>
/// <param name="store">The underlying store.</param>
public sealed class SagaRepository<TState>(ISagaStore store) : ISagaRepository<TState>
    where TState : class, ISagaState {
    private readonly ISagaStore _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>Gets the underlying store; useful when the dispatcher and the repository share it.</summary>
    public ISagaStore Store => _store;

    /// <inheritdoc />
    public async Task<SagaRepositoryEntry<TState>?> LoadAsync(string sagaId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(sagaId);

        var record = await _store.LoadAsync(sagaId, typeof(TState), cancellationToken).ConfigureAwait(false);

        if(record is null) {
            return null;
        }

        return record.State is TState state
            ? new SagaRepositoryEntry<TState>(record.SagaId, state, record.Version, record.ProgressState)
            : throw new InvalidOperationException(
                $"The stored saga state '{record.State.GetType().FullName}' is not '{typeof(TState).FullName}'.");
    }

    /// <inheritdoc />
    public Task SaveAsync(
        string sagaId,
        TState state,
        long expectedVersion,
        SagaProgress progress,
        CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(sagaId);
        Guard.NotNull(state);
        Guard.NotNull(progress);

        return _store.SaveAsync(sagaId, typeof(TState), state, expectedVersion, progress, cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string sagaId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(sagaId);

        return _store.DeleteAsync(sagaId, typeof(TState), cancellationToken);
    }
}