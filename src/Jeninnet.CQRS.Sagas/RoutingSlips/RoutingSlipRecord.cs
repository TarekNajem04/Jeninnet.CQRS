//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.RoutingSlips;

/// <summary>The persisted lifecycle of one routing slip.</summary>
public enum RoutingSlipStatus {
    /// <summary>Steps are executing.</summary>
    Running = 0,

    /// <summary>A step failed and completed steps are being compensated.</summary>
    Compensating = 1,

    /// <summary>Every step completed.</summary>
    Completed = 2,

    /// <summary>A step failed and every completed step was compensated.</summary>
    Compensated = 3,

    /// <summary>A step failed and at least one compensation failed; the slip is dead-lettered.</summary>
    Faulted = 4,
}

/// <summary>
/// Durable routing-slip row. The arguments payload is the strongly typed arguments serialized by
/// the configured <see cref="Serialization.IMessageSerializer"/>; progress is persisted after every
/// step and compensation so a restart resumes at the next operation.
/// </summary>
public sealed record RoutingSlipRecord {
    /// <summary>Gets the slip id.</summary>
    public string SlipId { get; init; } = string.Empty;

    /// <summary>Gets the definition name used to resolve activities after a restart.</summary>
    public string DefinitionName { get; init; } = string.Empty;

    /// <summary>Gets the assembly-qualified arguments type name.</summary>
    public string ArgumentsType { get; init; } = string.Empty;

    /// <summary>Gets the serialized arguments payload.</summary>
    public byte[] ArgumentsPayload { get; init; } = [];

    /// <summary>Gets the lifecycle status.</summary>
    public RoutingSlipStatus Status { get; init; }

    /// <summary>Gets the zero-based index of the next step to execute, or the failed step while compensating.</summary>
    public int NextStepIndex { get; init; }

    /// <summary>Gets the completed step names in execution order.</summary>
    public IReadOnlyList<string> CompletedSteps { get; init; } = [];

    /// <summary>Gets the compensated step names in completion order.</summary>
    public IReadOnlyList<string> CompensatedSteps { get; init; } = [];

    /// <summary>Gets the step whose execution failed, when any.</summary>
    public string? FailedStep { get; init; }

    /// <summary>Gets the failed step's exception type name.</summary>
    public string? FailureErrorType { get; init; }

    /// <summary>Gets the failed step's exception message.</summary>
    public string? FailureErrorMessage { get; init; }

    /// <summary>Gets the failed compensation's exception type name.</summary>
    public string? CompensationErrorType { get; init; }

    /// <summary>Gets the failed compensation's exception message.</summary>
    public string? CompensationErrorMessage { get; init; }

    /// <summary>Gets the tenant that started the slip, when any.</summary>
    public string? TenantId { get; init; }

    /// <summary>Gets when the slip was created, in UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Gets when the slip last changed, in UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; init; }

    /// <summary>Gets the optimistic-concurrency version.</summary>
    public long Version { get; init; }
}

/// <summary>Lightweight routing-slip row view for progress inspection and health checks.</summary>
/// <param name="SlipId">The slip id.</param>
/// <param name="DefinitionName">The definition name.</param>
/// <param name="Status">The lifecycle status.</param>
/// <param name="NextStepIndex">The next step index, or the failed step index while compensating.</param>
/// <param name="CompletedSteps">The completed step names.</param>
/// <param name="CompensatedSteps">The compensated step names.</param>
/// <param name="FailedStep">The failed step, when any.</param>
/// <param name="LastError">The last failure message, when any.</param>
/// <param name="UpdatedAtUtc">When the slip last changed.</param>
public sealed record RoutingSlipSnapshot(
    string SlipId,
    string DefinitionName,
    RoutingSlipStatus Status,
    int NextStepIndex,
    IReadOnlyList<string> CompletedSteps,
    IReadOnlyList<string> CompensatedSteps,
    string? FailedStep,
    string? LastError,
    DateTimeOffset UpdatedAtUtc);

/// <summary>The outcome of one execution or resume attempt.</summary>
/// <param name="SlipId">The slip id.</param>
/// <param name="DefinitionName">The definition name.</param>
/// <param name="Status">The final status.</param>
/// <param name="CompletedStepCount">How many steps completed.</param>
/// <param name="CompensatedStepCount">How many steps were compensated.</param>
/// <param name="FailedStep">The failed step, when any.</param>
/// <param name="Error">The step failure, when any.</param>
/// <param name="CompensationError">The compensation failure, when any.</param>
public sealed record RoutingSlipResult(
    string SlipId,
    string DefinitionName,
    RoutingSlipStatus Status,
    int CompletedStepCount,
    int CompensatedStepCount,
    string? FailedStep,
    Exception? Error,
    Exception? CompensationError);