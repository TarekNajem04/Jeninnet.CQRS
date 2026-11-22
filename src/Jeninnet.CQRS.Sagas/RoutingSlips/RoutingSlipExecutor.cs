//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.RoutingSlips;

/// <summary>
/// Executes a persisted routing slip: run steps in order, save progress after every step, and on
/// failure compensate completed steps in reverse order. A restart resumes from the last saved
/// operation, so a crash never replays a completed step or compensation.
/// </summary>
/// <remarks>
/// <para>
/// Steps and compensations resolve their activity from a fresh DI scope per attempt. The arguments
/// object is the only shared state; mutate it with anything a later step or compensation needs.
/// </para>
/// <para>
/// Per-step retry and timeout are applied per attempt. Cancellation of the caller's token stops the
/// slip without compensating and leaves the row <see cref="RoutingSlipStatus.Running"/> at the next
/// step, which is the graceful-shutdown/resume path. Activity failures compensate; a compensation
/// failure marks the row <see cref="RoutingSlipStatus.Faulted"/> and records one dead letter with
/// both errors.
/// </para>
/// </remarks>
public sealed class RoutingSlipExecutor {
    private readonly IServiceScopeFactory _scopes;
    private readonly IRoutingSlipStore _store;
    private readonly IMessageSerializer _serializer;
    private readonly RoutingSlipDefinitionRegistry _definitions;
    private readonly RoutingSlipMetrics _metrics;
    private readonly IDeadLetterStore _deadLetters;
    private readonly TimeProvider _clock;
    private readonly ITenantContext? _tenant;

    /// <summary>Initializes the executor.</summary>
    /// <param name="scopes">Creates a scope per step attempt.</param>
    /// <param name="store">The routing-slip store.</param>
    /// <param name="serializer">Serializes slip arguments for persistence.</param>
    /// <param name="definitions">The configured definitions.</param>
    /// <param name="metrics">The routing-slip instruments.</param>
    /// <param name="deadLetters">Receives slips whose compensation failed.</param>
    /// <param name="clock">Stamps rows; defaults to the system clock.</param>
    /// <param name="tenant">The ambient tenant, stamped on new slips and dead letters.</param>
    public RoutingSlipExecutor( // NOSONAR: Public options/records intentionally group related settings; splitting would fragment the API.
        IServiceScopeFactory scopes,
        IRoutingSlipStore store,
        IMessageSerializer serializer,
        RoutingSlipDefinitionRegistry definitions,
        RoutingSlipMetrics metrics,
        IDeadLetterStore deadLetters,
        TimeProvider? clock = null,
        ITenantContext? tenant = null) {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _deadLetters = deadLetters ?? throw new ArgumentNullException(nameof(deadLetters));
        _clock = clock ?? TimeProvider.System;
        _tenant = tenant;
    }

    /// <summary>Starts a new slip with a generated id.</summary>
    /// <typeparam name="TArguments">The strongly typed arguments.</typeparam>
    /// <param name="arguments">The slip arguments.</param>
    /// <param name="cancellationToken">Cancels the execution.</param>
    public Task<RoutingSlipResult> ExecuteAsync<TArguments>(TArguments arguments, CancellationToken cancellationToken = default)
        where TArguments : class =>
        ExecuteAsync(arguments, slipId: null, cancellationToken);

    /// <summary>Starts a new slip with an explicit id.</summary>
    /// <typeparam name="TArguments">The strongly typed arguments.</typeparam>
    /// <param name="arguments">The slip arguments.</param>
    /// <param name="slipId">The explicit id, or <see langword="null"/> for a generated one.</param>
    /// <param name="cancellationToken">Cancels the execution.</param>
    public Task<RoutingSlipResult> ExecuteAsync<TArguments>(TArguments arguments, string? slipId, CancellationToken cancellationToken = default)
        where TArguments : class {
        Guard.NotNull(arguments);

        var definition = _definitions.GetRequired(typeof(TArguments));

        return definition.ExecuteAsync(this, arguments, slipId, cancellationToken);
    }

    /// <summary>Resumes a persisted slip at its next step or compensation.</summary>
    /// <param name="slipId">The slip id.</param>
    /// <param name="cancellationToken">Cancels the execution.</param>
    /// <exception cref="InvalidOperationException">The slip id is unknown.</exception>
    public async Task<RoutingSlipResult> ResumeAsync(string slipId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(slipId);

        var record = await _store.LoadAsync(slipId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No routing slip with id '{slipId}' is persisted.");
        var definition = _definitions.GetRequired(record.DefinitionName);

        return await definition.ResumeAsync(this, record, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<RoutingSlipResult> RunAsync<TArguments>(
        RoutingSlipDefinition<TArguments> definition,
        TArguments arguments,
        string? slipId,
        RoutingSlipRecord? existing,
        CancellationToken cancellationToken) where TArguments : class {
        Guard.NotNull(definition);
        Guard.NotNull(arguments);

        var completed = existing is null ? [] : new List<string>(existing.CompletedSteps);
        var compensated = existing is null ? [] : new List<string>(existing.CompensatedSteps);
        var record = existing ?? await CreateAsync(definition, arguments, slipId, cancellationToken).ConfigureAwait(false);

        if(record.Status is RoutingSlipStatus.Completed or RoutingSlipStatus.Compensated or RoutingSlipStatus.Faulted) {
            return ToResult(record);
        }

        if(record.Status is RoutingSlipStatus.Compensating) {
            return await CompensateRemainingAsync(definition, arguments, record, completed, compensated, executionError: null, cancellationToken)
                .ConfigureAwait(false);
        }

        var steps = definition.TypedSteps;

        for(var index = record.NextStepIndex; index < steps.Count; index++) {
            var step = steps[index];

            try {
                await ExecuteActivityAsync(step, arguments, compensate: false, cancellationToken).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
                // Graceful shutdown/crash boundary: leave the row Running at this step.
                throw;
            }
            catch(Exception error) {
                _metrics.RecordStep(definition.Name, step.Name, compensation: false, completed: false);

                return await BeginCompensationAsync(
                    definition,
                    arguments,
                    record,
                    completed,
                    compensated,
                    index,
                    step.Name,
                    error,
                    cancellationToken).ConfigureAwait(false);
            }

            completed.Add(step.Name);
            record = await SaveProgressAsync(
                record,
                arguments,
                RoutingSlipStatus.Running,
                index + 1,
                completed,
                compensated,
                failedStep: null,
                failure: null,
                compensationFailure: null,
                cancellationToken).ConfigureAwait(false);
            _metrics.RecordStep(definition.Name, step.Name, compensation: false, completed: true);
        }

        record = await SaveProgressAsync(
            record,
            arguments,
            RoutingSlipStatus.Completed,
            steps.Count,
            completed,
            compensated,
            failedStep: null,
            failure: null,
            compensationFailure: null,
            cancellationToken).ConfigureAwait(false);
        _metrics.RecordCompleted(definition.Name);

        return ToResult(record);
    }

    internal TArguments Deserialize<TArguments>(RoutingSlipRecord record) where TArguments : class {
        Guard.NotNull(record);

        var expected = TypeName(typeof(TArguments));

        if(!string.Equals(record.ArgumentsType, expected, StringComparison.Ordinal)) {
            throw new InvalidOperationException(
                $"Routing slip '{record.SlipId}' stores arguments type '{record.ArgumentsType}', not '{expected}'.");
        }

        return _serializer.Deserialize(record.ArgumentsPayload, typeof(TArguments)) as TArguments
            ?? throw new InvalidOperationException(
                $"Routing slip '{record.SlipId}' arguments could not be deserialized as '{expected}'.");
    }

    private async Task<RoutingSlipRecord> CreateAsync<TArguments>(
        RoutingSlipDefinition<TArguments> definition,
        TArguments arguments,
        string? slipId,
        CancellationToken cancellationToken) where TArguments : class {
        var now = _clock.GetUtcNow();

        return await _store.CreateAsync(new RoutingSlipRecord {
            SlipId = slipId ?? Guid.NewGuid().ToString("N"),
            DefinitionName = definition.Name,
            ArgumentsType = TypeName(typeof(TArguments)),
            ArgumentsPayload = Serialize(arguments),
            Status = RoutingSlipStatus.Running,
            NextStepIndex = 0,
            CompletedSteps = [],
            CompensatedSteps = [],
            TenantId = _tenant?.Current,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RoutingSlipResult> BeginCompensationAsync<TArguments>( // NOSONAR: Public options/records intentionally group related settings; splitting would fragment the API.
        RoutingSlipDefinition<TArguments> definition,
        TArguments arguments,
        RoutingSlipRecord record,
        List<string> completed,
        List<string> compensated,
        int failedIndex,
        string failedStep,
        Exception error,
        CancellationToken cancellationToken) where TArguments : class {
        record = await SaveProgressAsync(
            record,
            arguments,
            RoutingSlipStatus.Compensating,
            failedIndex,
            completed,
            compensated,
            failedStep,
            error,
            compensationFailure: null,
            cancellationToken).ConfigureAwait(false);

        return await CompensateRemainingAsync(definition, arguments, record, completed, compensated, error, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<RoutingSlipResult> CompensateRemainingAsync<TArguments>(
        RoutingSlipDefinition<TArguments> definition,
        TArguments arguments,
        RoutingSlipRecord record,
        List<string> completed,
        List<string> compensated,
        Exception? executionError,
        CancellationToken cancellationToken) where TArguments : class {
        var failure = executionError ?? new RoutingSlipStepFailedException(
            record.SlipId,
            record.FailedStep ?? string.Empty,
            record.FailureErrorType ?? string.Empty,
            record.FailureErrorMessage ?? string.Empty);
        var stepsByName = definition.TypedSteps.ToDictionary(static step => step.Name, StringComparer.Ordinal);

        for(var index = completed.Count - 1; index >= 0; index--) {
            var stepName = completed[index];

            if(compensated.Contains(stepName, StringComparer.Ordinal)) {
                continue;
            }

            var step = stepsByName[stepName];

            try {
                await ExecuteActivityAsync(step, arguments, compensate: true, cancellationToken).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch(Exception compensationError) {
                _metrics.RecordStep(definition.Name, stepName, compensation: true, completed: false);
                var failureException = new RoutingSlipCompensationException(
                    record.SlipId,
                    record.FailedStep ?? string.Empty,
                    failure,
                    stepName,
                    compensationError);
                var failed = record with {
                    Status = RoutingSlipStatus.Faulted,
                    FailureErrorType = ErrorTypeOf(failure),
                    FailureErrorMessage = failure.Message,
                    CompensationErrorType = ErrorTypeOf(compensationError),
                    CompensationErrorMessage = compensationError.Message,
                    UpdatedAtUtc = _clock.GetUtcNow(),
                };
                failed = await _store.SaveAsync(failed, record.Version, cancellationToken).ConfigureAwait(false);
                await RecordDeadLetterAsync(failureException, failed, stepName).ConfigureAwait(false);
                _metrics.RecordFaulted(definition.Name);

                return ToResult(failed, failure, failureException);
            }

            compensated.Add(stepName);
            record = await SaveProgressAsync(
                record,
                arguments,
                RoutingSlipStatus.Compensating,
                record.NextStepIndex,
                completed,
                compensated,
                failedStep: null,
                failure: null,
                compensationFailure: null,
                cancellationToken).ConfigureAwait(false);
            _metrics.RecordStep(definition.Name, stepName, compensation: true, completed: true);
        }

        record = await SaveProgressAsync(
            record,
            arguments,
            RoutingSlipStatus.Compensated,
            record.NextStepIndex,
            completed,
            compensated,
            failedStep: null,
            failure: null,
            compensationFailure: null,
            cancellationToken).ConfigureAwait(false);
        _metrics.RecordCompensated(definition.Name);

        return ToResult(record, failure, compensationError: null);
    }

    private async Task<RoutingSlipRecord> SaveProgressAsync<TArguments>( // NOSONAR: Public options/records intentionally group related settings; splitting would fragment the API.
        RoutingSlipRecord record,
        TArguments arguments,
        RoutingSlipStatus status,
        int nextStepIndex,
        List<string> completed,
        List<string> compensated,
        string? failedStep,
        Exception? failure,
        Exception? compensationFailure,
        CancellationToken cancellationToken) where TArguments : class {
        var updated = record with {
            Status = status,
            NextStepIndex = nextStepIndex,
            CompletedSteps = [.. completed],
            CompensatedSteps = [.. compensated],
            ArgumentsPayload = Serialize(arguments),
            FailedStep = failedStep ?? record.FailedStep,
            FailureErrorType = failure is null ? record.FailureErrorType : ErrorTypeOf(failure),
            FailureErrorMessage = failure is null ? record.FailureErrorMessage : failure.Message,
            CompensationErrorType = compensationFailure is null ? record.CompensationErrorType : ErrorTypeOf(compensationFailure),
            CompensationErrorMessage = compensationFailure is null ? record.CompensationErrorMessage : compensationFailure.Message,
            UpdatedAtUtc = _clock.GetUtcNow(),
        };

        return await _store.SaveAsync(updated, record.Version, cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteActivityAsync<TArguments>(
        RoutingSlipStep<TArguments> step,
        TArguments arguments,
        bool compensate,
        CancellationToken cancellationToken) where TArguments : class {
        var attempts = compensate ? step.Options.CompensationMaxAttempts : step.Options.MaxAttempts;
        var retryDelay = compensate ? step.Options.CompensationRetryDelay : step.Options.RetryDelay;
        var timeout = compensate ? step.Options.CompensationTimeout : step.Options.Timeout;

        for(var attempt = 1; ; attempt++) { // NOSONAR: The loop exits via return/throw; the attempt counter drives backoff, not the stop condition.
            try {
                using var scope = _scopes.CreateScope();
                var activity = (IActivity<TArguments>)scope.ServiceProvider.GetRequiredService(step.ActivityType);
                await InvokeActivityAsync(activity, arguments, compensate, timeout, step.Name, cancellationToken).ConfigureAwait(false);

                return;
            }
            catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch when(attempt < attempts) {
                if(retryDelay > TimeSpan.Zero) {
                    await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private static async Task InvokeActivityAsync<TArguments>(
        IActivity<TArguments> activity,
        TArguments arguments,
        bool compensate,
        TimeSpan timeout,
        string stepName,
        CancellationToken cancellationToken) where TArguments : class {
        if(timeout == Timeout.InfiniteTimeSpan) {
            await InvokeAsync(activity, arguments, compensate, cancellationToken).ConfigureAwait(false);

            return;
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try {
            await InvokeAsync(activity, arguments, compensate, timeoutSource.Token).ConfigureAwait(false);
        }
        catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested) {
            throw new TimeoutException(
                $"Routing-slip {(compensate ? "compensation" : "step")} '{stepName}' timed out after {timeout}.");
        }
    }

    private static Task InvokeAsync<TArguments>(
        IActivity<TArguments> activity,
        TArguments arguments,
        bool compensate,
        CancellationToken cancellationToken) where TArguments : class =>
        compensate
            ? activity.CompensateAsync(arguments, cancellationToken)
            : activity.ExecuteAsync(arguments, cancellationToken);

    private async Task RecordDeadLetterAsync(
        RoutingSlipCompensationException failure,
        RoutingSlipRecord record,
        string compensationStep) {
        var info = new RoutingSlipDeadLetterInfo(
            failure.SlipId,
            record.DefinitionName,
            failure.FailedStep,
            ErrorTypeOf(failure.ExecutionError),
            failure.ExecutionError.Message,
            compensationStep,
            ErrorTypeOf(failure.CompensationError),
            failure.CompensationError.Message);
        var deadLetter = new RoutingSlipDeadLetter(info, _clock.GetUtcNow(), record.TenantId);

        try {
            await _deadLetters.RecordAsync(deadLetter, failure, _clock.GetUtcNow(), CancellationToken.None).ConfigureAwait(false);
        }
        catch {
            // Dead-lettering is best-effort; the Faulted row and result still carry both errors.
        }
    }

    private byte[] Serialize<TArguments>(TArguments arguments) where TArguments : class =>
        _serializer.Serialize(arguments, typeof(TArguments));

    private static string TypeName(Type type) =>
        type.AssemblyQualifiedName ?? type.FullName ?? type.Name;

    private static string ErrorTypeOf(Exception error) =>
        error.GetType().FullName ?? error.GetType().Name;

    private static RoutingSlipResult ToResult(
        RoutingSlipRecord record,
        Exception? error = null,
        Exception? compensationError = null) {
        if(error is null && record.FailureErrorType is not null &&
           record.Status is RoutingSlipStatus.Compensated or RoutingSlipStatus.Faulted) {
            error = new RoutingSlipStepFailedException(
                record.SlipId,
                record.FailedStep ?? string.Empty,
                record.FailureErrorType,
                record.FailureErrorMessage ?? string.Empty);
        }

        if(compensationError is null && record.Status is RoutingSlipStatus.Faulted && record.CompensationErrorType is not null) {
            var compensationStep = record.CompensatedSteps.Count > 0
                ? record.CompensatedSteps[record.CompensatedSteps.Count - 1]
                : record.FailedStep ?? string.Empty;
            compensationError = new RoutingSlipStepFailedException(
                record.SlipId,
                compensationStep,
                record.CompensationErrorType,
                record.CompensationErrorMessage ?? string.Empty);
        }

        return new RoutingSlipResult(
            record.SlipId,
            record.DefinitionName,
            record.Status,
            record.CompletedSteps.Count,
            record.CompensatedSteps.Count,
            record.FailedStep,
            error,
            compensationError);
    }
}