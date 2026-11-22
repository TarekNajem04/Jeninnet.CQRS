//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//

namespace Jeninnet.CQRS.Exceptions;

/// <summary>
/// Runs typed exception actions and handlers for a failed request: actions first (observe),
/// then handlers (may produce a response), most specific exception type first. When nothing
/// handles the failure, the original exception is rethrown with its stack trace preserved.
/// Cancellation is never handled: <see cref="OperationCanceledException"/> is rethrown by the
/// caller before reaching this policy.
/// </summary>
internal static class ExceptionPolicy {
    internal static async Task<TResponse> HandleAsync<TRequest, TResponse>(
        TRequest request,
        Exception exception,
        IServiceProvider provider,
        CancellationToken cancellationToken) {
        Guard.NotNull(exception);
        Guard.NotNull(provider);

        var actions = provider.GetServices<ICqrsExceptionAction<TRequest>>();
        var handlers = provider.GetServices<ICqrsExceptionHandler<TRequest, TResponse>>();

        foreach(var level in Levels(exception.GetType())) {
            foreach(var action in actions) {
                if(action.ExceptionType == level) {
                    await action.HandleAsync(request, exception, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        foreach(var level in Levels(exception.GetType())) {
            foreach(var handler in handlers) {
                if(handler.ExceptionType != level) {
                    continue;
                }

                var result = await handler.HandleAsync(request, exception, cancellationToken).ConfigureAwait(false);

                if(result.IsHandled) {
                    return result.Response!;
                }
            }
        }

        ExceptionDispatchInfo.Capture(exception).Throw();
        throw new InvalidOperationException("Unreachable: ExceptionDispatchInfo.Throw always throws.");
    }

    /// <summary>
    /// The exception type and its base classes, most specific first; interfaces are not probed.
    /// </summary>
    private static IEnumerable<Type> Levels(Type exceptionType) {
        for(var level = exceptionType; level is not null && level != typeof(object); level = level.BaseType) {
            yield return level;
        }
    }
}
