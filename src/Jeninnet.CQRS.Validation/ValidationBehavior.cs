//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Validation;

/// <summary>
/// Represents a CQRS pipeline behavior that executes all registered validators
/// for the request type before invoking the next behavior or handler in the pipeline.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<ISyncValidator<TRequest>> syncValidators,
    IEnumerable<IAsyncValidator<TRequest>> asyncValidators,
    ILogger<ValidationBehavior<TRequest, TResponse>> logger,
    IOptions<CqrsValidationOptions>? options = null
) : IPipelineBehavior<TRequest, TResponse> {
    private readonly ISyncValidator<TRequest>[] _syncValidators = syncValidators?.ToArray() ?? [];
    private readonly IAsyncValidator<TRequest>[] _asyncValidators = asyncValidators?.ToArray() ?? [];
    private readonly ILogger<ValidationBehavior<TRequest, TResponse>> _logger = logger;
    private readonly CqrsValidationOptions? _options = options?.Value;

    #region LoggerMessage
    private const string EVENT_NAME = "ValidationFailed";

    private readonly Action<ILogger, string, int, int, Exception?>? _handleStartLoggerMessage =
        logger is null
            ? null
            : LoggerMessage.Define<string, int, int>(
                LogLevel.Debug,
                new EventId(1, EVENT_NAME),
                "Starting validation for request type {RequestType}. Sync validators: {SyncCount}, Async validators: {AsyncCount}."
            );

    private readonly Action<ILogger, string, Exception?>? _handleEndLoggerMessage =
        logger is null
            ? null
            : LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(1, EVENT_NAME),
                "Validation succeeded for request type {RequestType}."
            );

    private readonly Action<ILogger, string, int, Exception?>? _warningLoggerMessage =
        logger is null
            ? null
            : LoggerMessage.Define<string, int>(
                LogLevel.Warning,
                new EventId(2, EVENT_NAME),
                "Validation failed for request type {RequestType} with {ErrorCount} error(s)."
            );

    private readonly Action<ILogger, string, string, ValidationErrorSeverity, Exception?>? _warningLoggerMessage_10 =
        logger is null
            ? null
            : LoggerMessage.Define<string, string, ValidationErrorSeverity>(
                LogLevel.Warning,
                new EventId(2, EVENT_NAME),
                "Validation error on member '{Member}': {Message} (Severity: {Severity})."
            );

    private readonly Action<ILogger, int, Exception?>? _warningLoggerMessage_GreaterThan_10 =
        logger is null
            ? null
            : LoggerMessage.Define<int>(
                LogLevel.Warning,
                new EventId(2, EVENT_NAME),
                "Validation produced a large number of errors ({ErrorCount}); detailed logging is suppressed."
            );

    private readonly Action<ILogger, string, string, Exception?>? _warningLoggerMessageRequest =
        logger is null
            ? null
            : LoggerMessage.Define<string, string>(
                LogLevel.Warning,
                new EventId(2, EVENT_NAME),
                "Validation failure occurred for request type {RequestType}. Request instance type: {InstanceType}."
            );
    #endregion

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResponse>> next,
        CancellationToken cancellationToken
    ) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(next);

        // Fast path: no validators at all.
        if(_syncValidators.Length is 0 && _asyncValidators.Length is 0) {
            return await next(request, cancellationToken).ConfigureAwait(false);
        }

        var requestTypeName = typeof(TRequest).FullName ?? typeof(TRequest).Name;

        if(_handleStartLoggerMessage is not null) {
            _handleStartLoggerMessage(
                _logger,
                requestTypeName,
                _syncValidators.Length,
                _asyncValidators.Length,
                null
            );
        }

        var validationResult = await ValidateRequestAsync(request, cancellationToken).ConfigureAwait(false);

        if(!validationResult.IsValid) {
            LogValidationFailure(request, requestTypeName, validationResult);

            // Opt-in result mode: map the failure to the response type and short-circuit
            // the handler plus all inner behaviors. The default stays exceptions.
            if(_options?.FailureStrategy is CqrsValidationFailureStrategy.Result) {
                var mapper = _options.GetResultMapper(typeof(TResponse))
                    ?? throw new InvalidOperationException(
                        $"No result mapper registered for response type {typeof(TResponse).Name}. " +
                        $"Register one with {nameof(CqrsValidationOptions)}.{nameof(CqrsValidationOptions.UseResultAdapter)}<{typeof(TResponse).Name}>().");

                if(mapper(validationResult) is TResponse response) {
                    return response;
                }

                throw new InvalidOperationException(
                    $"The result mapper for response type {typeof(TResponse).Name} returned no usable response.");
            }

            throw new ValidationException(validationResult.Errors);
        }

        if(_handleEndLoggerMessage is not null) {
            _handleEndLoggerMessage(_logger, requestTypeName, null);
        }

        return await next(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates the given request by executing all synchronous and asynchronous validators.
    /// </summary>
    /// <param name="request">The request instance to validate.</param>
    /// <param name="cancellationToken">The cancellation token to observe.</param>
    /// <returns>
    /// A <see cref="ValidationResult"/> representing the aggregate result of all validators.
    /// Returns <see cref="ValidationResult.Success"/> when no validation errors are produced.
    /// </returns>
    private async Task<ValidationResult> ValidateRequestAsync(
        TRequest request,
        CancellationToken cancellationToken
    ) {
        //    // Keep this method intentionally simple (low cognitive complexity):
        //    // 1. Execute sync validators
        //    // 2. Execute async validators
        //    // 3. Convert collected errors (if any) to a ValidationResult

        var syncErrors = ExecuteSyncValidators(request);
        var asyncErrors = await ExecuteAsyncValidatorsAsync(request, cancellationToken).ConfigureAwait(false);

        return MergeResults(syncErrors, asyncErrors);
    }

    private static ValidationResult MergeResults(List<ValidationError>? syncErrors, List<ValidationError>? asyncErrors) {
        var hasSync = syncErrors is { Count: > 0 };
        var hasAsync = asyncErrors is { Count: > 0 };

        if(!hasSync && !hasAsync) {
            return ValidationResult.Success;
        }

        var collector = new ValidationErrorCollector();

        if(hasSync) {
            collector.AddRange(syncErrors!);
        }

        if(hasAsync) {
            collector.AddRange(asyncErrors!);
        }

        return ValidationResult.FromErrors(collector);
    }

    /// <summary>
    /// Executes all synchronous validators for the given request.
    /// </summary>
    /// <param name="request">The request instance to validate.</param>
    private List<ValidationError>? ExecuteSyncValidators(TRequest request) {
        if(_syncValidators.Length is 0) {
            return null;
        }

        var errors = new List<ValidationError>();

        foreach(var validator in _syncValidators) {
            var result = validator.Validate(request);

            if(result.IsValid || result.Errors.Count is 0) {
                continue;
            }

            errors.AddRange(result.Errors);
        }

        return errors;
    }

    /// <summary>
    /// Executes all asynchronous validators for the given request.
    /// </summary>
    /// <param name="request">The request instance to validate.</param>
    /// <param name="cancellationToken">The cancellation token to observe.</param>
    private async Task<List<ValidationError>?> ExecuteAsyncValidatorsAsync(TRequest request, CancellationToken cancellationToken) {
        if(_asyncValidators.Length is 0) {
            return null;
        }

        List<ValidationError>? errors = null;

        // Sequential evaluation is usually desirable for validation (ordering, side effects).
        // If you ever want full parallelism, this is the place to consider Task.WhenAll.
        foreach(var validator in _asyncValidators) {
            var result = await validator.ValidateAsync(request, cancellationToken)
                                        .ConfigureAwait(false);

            if(result is { IsValid: true } or { Errors.Count: 0 }) {
                continue;
            }

            var resultErrors = result.Errors;
            var count = resultErrors.Count;

            errors ??= new List<ValidationError>(count);
            errors.AddRange(resultErrors);
        }

        return errors;
    }

    /// <summary>
    /// Logs validation failures in a structured and scalable way, including error details
    /// for small error sets and summarized information for large error sets.
    /// </summary>
    /// <param name="request">The request that failed validation.</param>
    /// <param name="requestTypeName">The type name of the request, precomputed for efficiency.</param>
    /// <param name="result">The validation result containing the error details.</param>
    private void LogValidationFailure(
        TRequest request,
        string requestTypeName,
        ValidationResult result) {
        var errorCount = result.Errors.Count;

        if(_warningLoggerMessage is not null) {
            _warningLoggerMessage(_logger, requestTypeName, errorCount, null);
        }

        LogValidationErrors(result, errorCount);
        LogRequestInfo(request, requestTypeName);
    }

    /// <summary>
    /// Logs individual or summarized validation errors depending on the error count.
    /// </summary>
    /// <param name="result">The validation result containing errors.</param>
    /// <param name="errorCount">The total number of errors.</param>
    private void LogValidationErrors(ValidationResult result, int errorCount) {
        if(errorCount is <= 0) {
            return;
        }

        if(errorCount is <= 10 && _warningLoggerMessage_10 is not null) {
            foreach(var error in result.Errors) {
                _warningLoggerMessage_10(
                    _logger,
                    error.MemberName,
                    error.Message ?? error.MessageId.Id,
                    error.Severity,
                    null
                );
            }

            return;
        }

        if(_warningLoggerMessage_GreaterThan_10 is not null) {
            _warningLoggerMessage_GreaterThan_10(_logger, errorCount, null);
        }
    }

    /// <summary>
    /// Logs basic request type information associated with a validation failure
    /// without logging the full payload to avoid leaking sensitive data.
    /// </summary>
    /// <param name="request">The request instance.</param>
    /// <param name="requestTypeName">The type name of the request.</param>
    private void LogRequestInfo(TRequest request, string requestTypeName) {
        if(request is null) {
            return;
        }

        var instanceTypeName = request.GetType().FullName ?? request.GetType().Name;

        if(_warningLoggerMessageRequest is not null) {
            _warningLoggerMessageRequest(
                _logger,
                requestTypeName,
                instanceTypeName,
                null
            );
        }
    }
}
