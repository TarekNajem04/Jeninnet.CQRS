//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.RoutingSlips;

/// <summary>
/// Thrown when a routing-slip save loses the optimistic-concurrency race or a slip id already
/// exists. The slip is not compensated; resume or inspect the durable row explicitly.
/// </summary>
public sealed class RoutingSlipConcurrencyException : InvalidOperationException {
    /// <summary>Initializes the exception for a lost version race.</summary>
    /// <param name="slipId">The slip id.</param>
    /// <param name="expectedVersion">The version the writer expected.</param>
    /// <param name="actualVersion">The version found, or -1 when an insert collided.</param>
    /// <param name="innerException">The provider exception, when available.</param>
    public RoutingSlipConcurrencyException(string slipId, long expectedVersion, long actualVersion, Exception? innerException = null)
        : base($"Routing slip '{slipId}' changed concurrently: expected version {expectedVersion}, found {actualVersion}.", innerException) {
        SlipId = slipId ?? throw new ArgumentNullException(nameof(slipId));
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    /// <summary>Gets the slip id.</summary>
    public string SlipId { get; }

    /// <summary>Gets the version the writer expected.</summary>
    public long ExpectedVersion { get; }

    /// <summary>Gets the version found, or -1 for an insert collision.</summary>
    public long ActualVersion { get; }
}

/// <summary>
/// A reconstructed step failure. Fresh executions surface the original exception; a resumed
/// compensation only has the persisted type/message, so it uses this exception to keep both
/// failures available without reflection.
/// </summary>
public sealed class RoutingSlipStepFailedException : InvalidOperationException {
    /// <summary>Initializes the exception from persisted error fields.</summary>
    /// <param name="slipId">The slip id.</param>
    /// <param name="stepName">The failed step.</param>
    /// <param name="originalErrorType">The original exception type name.</param>
    /// <param name="originalErrorMessage">The original exception message.</param>
    public RoutingSlipStepFailedException(string slipId, string stepName, string originalErrorType, string originalErrorMessage)
        : base($"Routing slip '{slipId}' step '{stepName}' failed: {originalErrorMessage}") {
        SlipId = slipId ?? throw new ArgumentNullException(nameof(slipId));
        StepName = stepName ?? string.Empty;
        OriginalErrorType = originalErrorType ?? string.Empty;
        OriginalErrorMessage = originalErrorMessage ?? string.Empty;
    }

    /// <summary>Gets the slip id.</summary>
    public string SlipId { get; }

    /// <summary>Gets the failed step name.</summary>
    public string StepName { get; }

    /// <summary>Gets the original exception type name.</summary>
    public string OriginalErrorType { get; }

    /// <summary>Gets the original exception message.</summary>
    public string OriginalErrorMessage { get; }
}

/// <summary>
/// Both failures of a slip whose compensation also failed. The message and the inner aggregate
/// carry the execution error and the compensation error so the dead-letter row is actionable.
/// </summary>
public sealed class RoutingSlipCompensationException : InvalidOperationException {
    /// <summary>Initializes the exception with both errors.</summary>
    /// <param name="slipId">The slip id.</param>
    /// <param name="failedStep">The step whose execution failed.</param>
    /// <param name="executionError">The execution failure.</param>
    /// <param name="compensationStep">The step whose compensation failed.</param>
    /// <param name="compensationError">The compensation failure.</param>
    public RoutingSlipCompensationException(
        string slipId,
        string failedStep,
        Exception executionError,
        string compensationStep,
        Exception compensationError)
        : base(
            BuildMessage(slipId, failedStep, executionError, compensationStep, compensationError),
            new AggregateException(executionError, compensationError)) {
        SlipId = slipId;
        FailedStep = failedStep ?? string.Empty;
        ExecutionError = executionError;
        CompensationStep = compensationStep ?? string.Empty;
        CompensationError = compensationError;
    }

    private static string BuildMessage(
        string slipId,
        string failedStep,
        Exception executionError,
        string compensationStep,
        Exception compensationError) {
        Guard.NotNull(slipId);
        Guard.NotNull(executionError);
        Guard.NotNull(compensationError);

        return $"Routing slip '{slipId}' step '{failedStep}' failed and compensation '{compensationStep}' also failed. " +
            $"Execution: {executionError.Message} Compensation: {compensationError.Message}";
    }

    /// <summary>Gets the slip id.</summary>
    public string SlipId { get; }

    /// <summary>Gets the step whose execution failed.</summary>
    public string FailedStep { get; }

    /// <summary>Gets the execution failure.</summary>
    public Exception ExecutionError { get; }

    /// <summary>Gets the step whose compensation failed.</summary>
    public string CompensationStep { get; }

    /// <summary>Gets the compensation failure.</summary>
    public Exception CompensationError { get; }
}
