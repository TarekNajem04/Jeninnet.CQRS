//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.RoutingSlips;

/// <summary>Both failures of a routing slip whose compensation also failed.</summary>
/// <param name="SlipId">The slip id.</param>
/// <param name="DefinitionName">The definition name.</param>
/// <param name="FailedStep">The step whose execution failed.</param>
/// <param name="FailureErrorType">The execution exception type name.</param>
/// <param name="FailureErrorMessage">The execution exception message.</param>
/// <param name="CompensationStep">The step whose compensation failed.</param>
/// <param name="CompensationErrorType">The compensation exception type name.</param>
/// <param name="CompensationErrorMessage">The compensation exception message.</param>
public sealed record RoutingSlipDeadLetterInfo(
    string SlipId,
    string DefinitionName,
    string FailedStep,
    string FailureErrorType,
    string FailureErrorMessage,
    string CompensationStep,
    string CompensationErrorType,
    string CompensationErrorMessage);

/// <summary>
/// Dead-letter envelope for a routing slip whose compensation failed. It preserves both errors
/// and the slip identity for inspection. Replay deliberately fails: resuming a slip is an
/// explicit operator action after fixing the activity, not a command dispatch.
/// </summary>
public sealed class RoutingSlipDeadLetter : IDeferredEnvelope {
    /// <summary>Initializes the dead-letter envelope.</summary>
    /// <param name="info">The slip and both errors.</param>
    /// <param name="failedAtUtc">When the compensation failure was observed.</param>
    /// <param name="tenantId">The tenant that started the slip, when any.</param>
    public RoutingSlipDeadLetter(RoutingSlipDeadLetterInfo info, DateTimeOffset failedAtUtc, string? tenantId = null) {
        Info = info ?? throw new ArgumentNullException(nameof(info));
        DueUtc = failedAtUtc;
        TenantId = tenantId;
    }

    /// <summary>Gets the slip and both failures.</summary>
    public RoutingSlipDeadLetterInfo Info { get; }

    /// <inheritdoc />
    public object Command => Info;

    /// <inheritdoc />
    public int Version => 1;

    /// <inheritdoc />
    public DateTimeOffset DueUtc { get; }

    /// <inheritdoc />
    public DateTimeOffset? ExpiresAtUtc => null;

    /// <inheritdoc />
    public string? TenantId { get; }

    /// <inheritdoc />
    public IDeferredEnvelope WithTenant(string? tenantId) =>
        new RoutingSlipDeadLetter(Info, DueUtc, tenantId);

    /// <inheritdoc />
    public Task DispatchAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            $"Routing slip '{Info.SlipId}' is dead-lettered because compensation '{Info.CompensationStep}' failed. " +
            "Fix the activity, then resume the slip explicitly; a generic dead-letter replay cannot continue a partially compensated slip.");
}
