//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Behaviors;

/// <summary>
/// Catches exceptions from downstream, logs them, then rethrows or wraps.
/// </summary>
public sealed class ErrorHandlingBehavior<TRequest, TResponse>(ILogger<ErrorHandlingBehavior<TRequest, TResponse>> logger) : IOrderedPipelineBehavior<TRequest, TResponse> {
    private readonly ILogger<ErrorHandlingBehavior<TRequest, TResponse>> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    int IOrderedPipelineBehavior<TRequest, TResponse>.Order => PipelineOrder.ERROR_HANDLING;

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(TRequest request, Func<TRequest, CancellationToken, Task<TResponse>> next, CancellationToken cancellationToken) {
        try {
            return await next(request, cancellationToken).ConfigureAwait(false);
        }
        catch(Exception ex) {
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(0, "ErrorHandling"), "Error handling {Request}")(_logger, typeof(TRequest).Name, ex);
            throw;
        }
    }
}
