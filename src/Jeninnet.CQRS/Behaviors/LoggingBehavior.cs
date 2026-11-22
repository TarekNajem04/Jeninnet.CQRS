//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Behaviors;

/// <summary>
/// Provides logging behavior for a pipeline, logging information before and after the handling of a request.
/// </summary>
/// <remarks>
/// This behavior logs messages at the <see cref="Microsoft.Extensions.Logging.LogLevel"/> <c>Information</c> level both before and after the request is processed.
/// It uses the <see cref="ILogger"/> instance provided to log the type of the request being handled.
/// </remarks>
/// <typeparam name="TRequest">The type of the request being handled.</typeparam>
/// <typeparam name="TResponse">The type of the response returned after handling the request.</typeparam>
/// <param name="logger"> </param>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger) : IOrderedPipelineBehavior<TRequest, TResponse> {
    /// <inheritdoc />
    int IOrderedPipelineBehavior<TRequest, TResponse>.Order => PipelineOrder.LOGGING;

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(
        TRequest request,
        Func<TRequest,
            CancellationToken, Task<TResponse>> next,
        CancellationToken cancellationToken
    ) {
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(0, "HandlingRequest"),
            "Handling {Request}"
        )(logger, typeof(TRequest).Name, null);

        var response = await next(request, cancellationToken);

        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1, "HandledRequest"),
            "Handled {Request}"
        )(logger, typeof(TRequest).Name, null);

        return response;
    }
}
