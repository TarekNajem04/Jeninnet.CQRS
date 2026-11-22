//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Behaviors;

/// <summary>
/// Measures and logs the duration of each request.
/// </summary>
public sealed class MetricsBehavior<TRequest, TResponse>(ILogger<MetricsBehavior<TRequest, TResponse>> logger) : IOrderedPipelineBehavior<TRequest, TResponse> {
    private readonly ILogger<MetricsBehavior<TRequest, TResponse>> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    int IOrderedPipelineBehavior<TRequest, TResponse>.Order => PipelineOrder.METRICS;

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResponse>> next,
        CancellationToken cancellationToken) {
        var stopwatch = Stopwatch.StartNew();
        var response = await next(request, cancellationToken)
                               .ConfigureAwait(false);
        stopwatch.Stop();

        LoggerMessage.Define<string, long>(LogLevel.Information, new EventId(0, "Metrics"), "{Request} handled in {ElapsedMilliseconds}ms")(
            _logger,
            typeof(TRequest).Name,
            stopwatch.ElapsedMilliseconds,
            /*Exception:*/ null
        );

        return response;
    }
}