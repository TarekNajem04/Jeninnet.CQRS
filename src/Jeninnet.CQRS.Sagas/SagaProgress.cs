//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas;

/// <summary>Durable progress beside the saga state: which compensable steps completed and which
/// compensations already ran. Keeping it separate from the user's state type lets restart resume a
/// partially compensated saga without replaying completed undo commands.</summary>
/// <param name="CompletedSteps">Compensable step ids in the order they completed.</param>
/// <param name="CompensatedSteps">Step ids whose compensation already ran.</param>
public sealed record SagaProgress(
    IReadOnlyList<string> CompletedSteps,
    IReadOnlyCollection<string> CompensatedSteps) {
    /// <summary>Gets the empty progress used by new sagas and stores without a progress column.</summary>
    public static SagaProgress Empty { get; } = new([], new HashSet<string>(StringComparer.Ordinal));
}

/// <summary>A lightweight saga row view for health checks and metrics.</summary>
/// <param name="SagaId">The correlation id.</param>
/// <param name="StateType">The concrete state type.</param>
/// <param name="CurrentState">The persisted state name.</param>
/// <param name="UpdatedAtUtc">When the row last changed.</param>
public sealed record SagaSnapshot(string SagaId, Type StateType, string CurrentState, DateTimeOffset UpdatedAtUtc);