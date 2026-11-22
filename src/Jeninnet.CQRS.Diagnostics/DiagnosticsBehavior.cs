//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Diagnostics;

/// <summary>
/// A pipeline behavior that wraps handler execution in an OpenTelemetry span and records
/// dispatch duration and errors. Runs outermost among the built-in behaviors so the
/// measurements cover the whole pipeline.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
/// Initializes a new instance with the given diagnostics options.
/// </remarks>
/// <param name="options">The diagnostics options.</param>
public sealed class DiagnosticsBehavior<TRequest, TResponse>(IOptions<CqrsDiagnosticsOptions> options) : IOrderedPipelineBehavior<TRequest, TResponse> {
    private readonly bool _tracing = (options ?? throw new ArgumentNullException(nameof(options))).Value.EnableTracing;
    private readonly bool _metrics = options.Value.EnableMetrics;

    /// <inheritdoc />
    int IOrderedPipelineBehavior<TRequest, TResponse>.Order => PipelineOrder.DIAGNOSTICS;

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(TRequest request, Func<TRequest, CancellationToken, Task<TResponse>> next, CancellationToken cancellationToken) {
        // Fast path: everything disabled means one await and nothing else.
        if(!_tracing && !_metrics) {
            return await next(request, cancellationToken).ConfigureAwait(false);
        }

        var requestType = typeof(TRequest).FullName ?? typeof(TRequest).Name;
        var startTimestamp = _metrics ? Stopwatch.GetTimestamp() : 0;

        using var activity = _tracing
            ? CqrsDiagnostics.ActivitySource.StartActivity(
                CqrsDiagnosticNames.DISPATCH_ACTIVITY,
                ActivityKind.Internal,
                parentContext: default,
                tags: [
                    new(CqrsDiagnosticNames.REQUEST_TYPE_TAG, requestType),
                    new(CqrsDiagnosticNames.RESPONSE_TYPE_TAG, typeof(TResponse).FullName ?? typeof(TResponse).Name)
                ])
            : null;

        try {
            var response = await next(request, cancellationToken).ConfigureAwait(false);

            activity?.SetStatus(ActivityStatusCode.Ok);
            Record(requestType, outcome: "success", exceptionType: null, startTimestamp);

            return response;
        }
        catch(OperationCanceledException exception) {
            activity?.SetStatus(ActivityStatusCode.Unset);
            AddException(activity, exception);
            Record(requestType, outcome: "cancelled", exceptionType: null, startTimestamp);

            throw;
        }
        catch(Exception exception) {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            AddException(activity, exception);
            Record(requestType, outcome: "failure", exceptionType: exception.GetType().FullName, startTimestamp);

            throw;
        }
    }

    private static void AddException(Activity? activity, Exception exception) =>
        activity?.AddEvent(new ActivityEvent(
            "exception",
            tags: new ActivityTagsCollection {
                ["exception.message"] = exception.Message,
                ["exception.type"] = exception.GetType().FullName
            }));

    private void Record(string requestType, string outcome, string? exceptionType, long startTimestamp) {
        if(!_metrics) {
            return;
        }

        var elapsedMs = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
        var tags = new TagList {
            { CqrsDiagnosticNames.REQUEST_TYPE_TAG, requestType },
            { CqrsDiagnosticNames.OUTCOME_TAG, outcome }
        };

        CqrsDiagnostics.DispatchDuration.Record(elapsedMs, tags);

        if(exceptionType is not null) {
            tags.Add(CqrsDiagnosticNames.EXCEPTION_TYPE_TAG, exceptionType);
            CqrsDiagnostics.DispatchErrors.Add(1, tags);
        }
    }
}
