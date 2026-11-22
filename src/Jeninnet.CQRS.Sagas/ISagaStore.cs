//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas;

/// <summary>
/// Saga persistence seam. The core package ships an in-memory implementation; the EF Core package
/// replaces it with a durable one. A save must be atomic: the expected version is checked and the
/// new version written together, or <see cref="SagaConcurrencyException"/> is thrown.
/// </summary>
/// <remarks>
/// State instances are deserialized fresh on every <see cref="LoadAsync"/>. One row is keyed by
/// the state type and saga id, so two machines may reuse the same correlation id without sharing
/// state.
/// </remarks>
public interface ISagaStore {
    /// <summary>Loads the saga row, or returns <see langword="null"/> when the id is unknown.</summary>
    /// <param name="sagaId">The correlation id.</param>
    /// <param name="stateType">The concrete state type.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<SagaRecord?> LoadAsync(string sagaId, Type stateType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the state only when <paramref name="expectedVersion"/> still matches. A new row uses
    /// version 0; a successful save writes version + 1.
    /// </summary>
    /// <param name="sagaId">The correlation id.</param>
    /// <param name="stateType">The concrete state type.</param>
    /// <param name="state">The mutated state.</param>
    /// <param name="expectedVersion">The version read before the transition.</param>
    /// <param name="progress">Compensation progress to persist with the state.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="SagaConcurrencyException">Another worker wrote the row first.</exception>
    Task SaveAsync(
        string sagaId,
        Type stateType,
        ISagaState state,
        long expectedVersion,
        SagaProgress progress,
        CancellationToken cancellationToken = default);

    /// <summary>Counts the persisted rows for one state type; used by the active gauge.</summary>
    /// <param name="stateType">The concrete state type.</param>
    /// <param name="cancellationToken">Cancels the count.</param>
    Task<long> CountAsync(Type stateType, CancellationToken cancellationToken = default);

    /// <summary>Lists lightweight row views for health checks.</summary>
    /// <param name="stateType">The concrete state type.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<SagaSnapshot>> ListAsync(Type stateType, CancellationToken cancellationToken = default);

    /// <summary>Removes a saga row; a missing row is a no-op.</summary>
    /// <param name="sagaId">The correlation id.</param>
    /// <param name="stateType">The concrete state type.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    Task DeleteAsync(string sagaId, Type stateType, CancellationToken cancellationToken = default);
}