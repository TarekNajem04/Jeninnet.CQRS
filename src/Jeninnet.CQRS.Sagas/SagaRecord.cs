//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas;

/// <summary>
/// Persisted saga row returned by <see cref="ISagaStore"/>. The state instance is deserialized
/// fresh for every load, so a failed transition cannot mutate the stored copy.
/// </summary>
/// <param name="SagaId">The correlation id assigned by the machine.</param>
/// <param name="StateType">The concrete state type.</param>
/// <param name="State">The deserialized state.</param>
/// <param name="Version">The row version used for optimistic concurrency.</param>
/// <param name="Progress">Compensation progress; null means empty.</param>
public sealed record SagaRecord(string SagaId, Type StateType, ISagaState State, long Version, SagaProgress? Progress = null) {
    /// <summary>Gets the progress with the empty fallback applied.</summary>
    public SagaProgress ProgressState => Progress ?? SagaProgress.Empty;
}
