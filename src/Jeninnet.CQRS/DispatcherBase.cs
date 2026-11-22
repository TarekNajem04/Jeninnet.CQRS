//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS;

/// <summary> Initializes a new instance of dispatcher.</summary>
/// <param name="provider"> The service provider from which handlers and behaviors will be resolved.</param>
/// <exception cref="ArgumentNullException"> Thrown if <paramref name="provider"/> is <see langword="null"/>.</exception>
public class DispatcherBase(IServiceProvider provider) {
    /// <summary>Gets the provider used to resolve handlers, behaviors, and processors.</summary>
    protected IServiceProvider Provider { get; } = provider ?? throw new ArgumentNullException(nameof(provider));

    /// <summary>
    /// Builds and invokes the pipeline of behaviors, ending with the final delegate.
    /// </summary>
    /// <typeparam name="TRequest">The request type (command or query).</typeparam>
    /// <typeparam name="TResponse">The response type (Unit for commands, TResult for queries).</typeparam>
    /// <param name="request">The request instance to dispatch.</param>
    /// <param name="final">
    /// The terminal delegate which actually invokes the handler.
    /// </param>
    /// <param name="behaviors">
    /// The ordered list of pipeline behaviors to run around the handler.
    /// </param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None"/>.</param>
    /// <returns>
    /// A task representing the pipeline execution. Yields the final response.
    /// </returns>
    protected static Task<TResponse> ExecutePipeline<TRequest, TResponse>(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResponse>> final,
        IReadOnlyList<IPipelineBehavior<TRequest, TResponse>> behaviors,
        CancellationToken cancellationToken
    ) {
        // Start with the innermost delegate
        var pipeline = final;
        // Wrap each behavior around the pipeline
        for(var i = behaviors.Count - 1; i >= 0; i--) {
            var behavior = behaviors[i];
            var next = pipeline;
            pipeline = (req, ct) => behavior.HandleAsync(req, next, ct);
        }

        // Invoke the composed pipeline
        return pipeline(request, cancellationToken);
    }

    /// <summary>Runs every <see cref="IPreProcessor{TRequest}"/> for the request.</summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <param name="request">The request instance.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    protected async Task ExecutePreProcessorAsync<TRequest>(TRequest request, CancellationToken cancellationToken) {
        var pre = Provider.GetServices<IPreProcessor<TRequest>>();
        foreach(var p in pre) {
            await p.ProcessAsync(request, cancellationToken)
                   .ConfigureAwait(false);
        }
    }

    /// <summary>Runs every <see cref="IPostProcessor{TRequest, TResponse}"/> for the result.</summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="request">The request instance.</param>
    /// <param name="result">The response produced by the pipeline.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    protected async Task ExecutePostProcessorAsync<TRequest, TResponse>(TRequest request, TResponse result, CancellationToken cancellationToken) {
        var post = Provider.GetServices<IPostProcessor<TRequest, TResponse>>();
        foreach(var p in post) {
            await p.ProcessAsync(request, result, cancellationToken)
                   .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Invokes a void‐returning command handler, wrapping its result in a <see cref="Unit"/>.
    /// </summary>
    /// <typeparam name="TRequest">The request type (command or query).</typeparam>
    /// <typeparam name="TResponse">The response type (Unit for commands, TResult for queries).</typeparam>
    /// <typeparam name="THandler">The handler type.</typeparam>
    /// <param name="handler">The handler instance.</param>
    /// <param name="command">The command to process.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None"/>.</param>
    /// <returns>
    /// A completed <see cref="Unit"/> after the handler finishes.
    /// </returns>
    protected static async Task<TResponse> InvokeHandlerAsync<TRequest, TResponse, THandler>(THandler handler, TRequest command, CancellationToken cancellationToken)
        where THandler : IHandler<TRequest, TResponse> {
        var response = await handler.HandleAsync(command, cancellationToken)
                                    .ConfigureAwait(false);
        return response;
    }

    /// <summary>
    /// The core execution engine for all CQRS requests.
    /// Handles Pre-processing, Pipeline Behaviors, Handler execution, and Post-processing.
    /// </summary>
    protected async Task<TResponse> ExecuteAsync<THandler, TRequest, TResponse>(TRequest request, CancellationToken cancellationToken)
        where THandler : IHandler<TRequest, TResponse> {
        Guard.NotNull(request);

        // Resolve the command handler
        var handler = Provider.GetService<THandler>()
                      ?? throw new InvalidOperationException($"No handler registered for {typeof(TRequest).Name}");

        return await ExecuteWithExceptionPolicyAsync(handler, request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a resolved handler through the pipeline and applies typed exception actions/handlers
    /// when the pipeline throws. Retries inside the pipeline run first; the policy sees only the
    /// final failure. Cancellation is never intercepted.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="handler">The resolved handler.</param>
    /// <param name="request">The request instance.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    protected async Task<TResponse> ExecuteWithExceptionPolicyAsync<TRequest, TResponse>(IHandler<TRequest, TResponse> handler, TRequest request, CancellationToken cancellationToken) {
        try {
            return await ExecuteCoreAsync(handler, request, cancellationToken).ConfigureAwait(false);
        }
        catch(OperationCanceledException) {
            throw;
        }
        catch(Exception exception) {
            return await ExceptionPolicy.HandleAsync<TRequest, TResponse>(request, exception, Provider, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs an already-resolved handler through pre-processors, the behavior pipeline,
    /// and post-processors. Use this when the handler was resolved by non-standard means
    /// (for example the one-generic fallback in command dispatch).
    /// </summary>
    protected async Task<TResponse> ExecuteCoreAsync<TRequest, TResponse>(IHandler<TRequest, TResponse> handler, TRequest request, CancellationToken cancellationToken) {
        Guard.NotNull(handler);
        Guard.NotNull(request);

        // Resolve any pipeline behaviors for (TCommand → TResult)
        var behaviors = Provider.GetServices<IPipelineBehavior<TRequest, TResponse>>()
                                .OrderBy(b => b switch {
                                    IOrderedPipelineBehavior<TRequest, TResponse> ordered => ordered.Order,
                                    _ => PipelineOrder.CUSTOM // Default for user-defined behaviors
                                })
                                .ToList();
        // Pre-processors
        await ExecutePreProcessorAsync(request, cancellationToken).ConfigureAwait(false);

        // Execute behaviors + handler
        var result = await ExecutePipeline(request, Final, behaviors, cancellationToken).ConfigureAwait(false);

        // Post-processors
        await ExecutePostProcessorAsync(request, result, cancellationToken).ConfigureAwait(false);

        return result;

        // Final delegate is the handler itself
        Task<TResponse> Final(TRequest command, CancellationToken token) => handler.HandleAsync(command, token);
    }
}
