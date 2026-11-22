//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.EntityFrameworkCore;

/// <summary>
/// One durable routing-slip row: the persisted arguments, ordered step progress, both failure
/// records, and the optimistic-concurrency version.
/// </summary>
public sealed class RoutingSlipEntity {
    /// <summary>Gets or sets the slip id. Primary key.</summary>
    public string SlipId { get; set; } = string.Empty;

    /// <summary>Gets or sets the definition name used to resume.</summary>
    public string DefinitionName { get; set; } = string.Empty;

    /// <summary>Gets or sets the assembly-qualified arguments type name.</summary>
    public string ArgumentsType { get; set; } = string.Empty;

    /// <summary>Gets or sets the serialized arguments payload.</summary>
    public byte[] ArgumentsPayload { get; set; } = [];

    /// <summary>Gets or sets the lifecycle status as an integer.</summary>
    public int Status { get; set; }

    /// <summary>Gets or sets the next step index, or the failed index while compensating.</summary>
    public int NextStepIndex { get; set; }

    /// <summary>Gets or sets the completed step names as JSON.</summary>
    public string CompletedSteps { get; set; } = "[]";

    /// <summary>Gets or sets the compensated step names as JSON.</summary>
    public string CompensatedSteps { get; set; } = "[]";

    /// <summary>Gets or sets the step whose execution failed.</summary>
    public string? FailedStep { get; set; }

    /// <summary>Gets or sets the execution error type name.</summary>
    public string? FailureErrorType { get; set; }

    /// <summary>Gets or sets the execution error message.</summary>
    public string? FailureErrorMessage { get; set; }

    /// <summary>Gets or sets the compensation error type name.</summary>
    public string? CompensationErrorType { get; set; }

    /// <summary>Gets or sets the compensation error message.</summary>
    public string? CompensationErrorMessage { get; set; }

    /// <summary>Gets or sets the tenant that started the slip.</summary>
    public string? TenantId { get; set; }

    /// <summary>Gets or sets the optimistic-concurrency version.</summary>
    public long Version { get; set; }

    /// <summary>Gets or sets when the slip was created, in UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Gets or sets when the slip last changed, in UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}