//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas;

/// <summary>Convenience overloads for <see cref="ISagaStore"/>.</summary>
public static class SagaStoreExtensions {
    /// <summary>Saves the state with empty compensation progress.</summary>
    /// <param name="store">The saga store.</param>
    /// <param name="sagaId">The correlation id.</param>
    /// <param name="stateType">The concrete state type.</param>
    /// <param name="state">The mutated state.</param>
    /// <param name="expectedVersion">The version read before the transition.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public static Task SaveAsync(
        this ISagaStore store,
        string sagaId,
        Type stateType,
        ISagaState state,
        long expectedVersion,
        CancellationToken cancellationToken = default) {
        Guard.NotNull(store);

        return store.SaveAsync(sagaId, stateType, state, expectedVersion, SagaProgress.Empty, cancellationToken);
    }
}
