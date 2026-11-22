//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.AspNetCore;

/// <summary>
/// Minimal-API mapping helpers over <see cref="ICqrsBroker"/>. Each helper is a thin adapter:
/// binding, dispatch, and <c>ProblemDetails</c> mapping only; authorization and other endpoint
/// metadata compose normally through the returned <see cref="RouteHandlerBuilder"/>.
/// </summary>
public static class CqrsEndpointRouteBuilderExtensions {
    /// <summary>
    /// Maps a command that returns a result. The command binds from a JSON body overlaid with
    /// route values (route wins) and query-string values.
    /// </summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The route pattern.</param>
    /// <param name="method">The HTTP method; defaults to POST.</param>
    /// <returns>The route handler builder for further conventions.</returns>
    public static RouteHandlerBuilder MapCqrsCommand<TCommand, TResult>(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        string method = "POST")
        where TCommand : class, ICommand<TResult> {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        return endpoints.MapMethods(pattern, [method], static async (HttpContext context) => {
            var binding = await CqrsRequestBinder.BindAsync<TCommand>(context, readBody: true, context.RequestAborted).ConfigureAwait(false);

            if(binding.Error is not null) {
                return binding.Error;
            }

            try {
                var result = await context.RequestServices
                    .GetRequiredService<ICqrsBroker>()
                    .DispatchAsync<TCommand, TResult>(binding.Request!, context.RequestAborted)
                    .ConfigureAwait(false);

                return Results.Ok(result);
            }
            catch(Exception exception) {
                return CqrsEndpointResults.MapException(context, exception);
            }
        }).Produces<TResult>();
    }

    /// <summary>
    /// Maps a void command. The command binds like
    /// <see cref="MapCqrsCommand{TCommand, TResult}"/>; success returns 204.
    /// </summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The route pattern.</param>
    /// <param name="method">The HTTP method; defaults to POST.</param>
    /// <returns>The route handler builder for further conventions.</returns>
    public static RouteHandlerBuilder MapCqrsCommand<TCommand>(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        string method = "POST")
        where TCommand : class, ICommand {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        return endpoints.MapMethods(pattern, [method], static async (HttpContext context) => {
            var binding = await CqrsRequestBinder.BindAsync<TCommand>(context, readBody: true, context.RequestAborted).ConfigureAwait(false);

            if(binding.Error is not null) {
                return binding.Error;
            }

            try {
                await context.RequestServices
                    .GetRequiredService<ICqrsBroker>()
                    .DispatchAsync(binding.Request!, context.RequestAborted)
                    .ConfigureAwait(false);

                return Results.NoContent();
            }
            catch(Exception exception) {
                return CqrsEndpointResults.MapException(context, exception);
            }
        }).Produces(StatusCodes.Status204NoContent);
    }

    /// <summary>
    /// Maps a query bound from route and query-string values.
    /// </summary>
    /// <typeparam name="TQuery">The query type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The route pattern.</param>
    /// <returns>The route handler builder for further conventions.</returns>
    public static RouteHandlerBuilder MapCqrsQuery<TQuery, TResult>(
        this IEndpointRouteBuilder endpoints,
        string pattern)
        where TQuery : class, IQuery<TResult> {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        return endpoints.MapGet(pattern, static async (HttpContext context) => {
            var binding = await CqrsRequestBinder.BindAsync<TQuery>(context, readBody: false, context.RequestAborted).ConfigureAwait(false);

            if(binding.Error is not null) {
                return binding.Error;
            }

            try {
                var result = await context.RequestServices
                    .GetRequiredService<ICqrsBroker>()
                    .DispatchAsync<TQuery, TResult>(binding.Request!, context.RequestAborted)
                    .ConfigureAwait(false);

                return Results.Ok(result);
            }
            catch(Exception exception) {
                return CqrsEndpointResults.MapException(context, exception);
            }
        }).Produces<TResult>();
    }

    /// <summary>
    /// Maps a streaming query bound from route and query-string values. The sequence is written
    /// as a JSON array without buffering.
    /// </summary>
    /// <typeparam name="TQuery">The stream query type.</typeparam>
    /// <typeparam name="TItem">The streamed item type.</typeparam>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The route pattern.</param>
    /// <returns>The route handler builder for further conventions.</returns>
    public static RouteHandlerBuilder MapCqrsStream<TQuery, TItem>(
        this IEndpointRouteBuilder endpoints,
        string pattern)
        where TQuery : class, IStreamQuery<TItem> {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        return endpoints.MapGet(pattern, static async (HttpContext context) => {
            var binding = await CqrsRequestBinder.BindAsync<TQuery>(context, readBody: false, context.RequestAborted).ConfigureAwait(false);

            if(binding.Error is not null) {
                return binding.Error;
            }

            try {
                var items = context.RequestServices
                    .GetRequiredService<ICqrsBroker>()
                    .StreamAsync<TQuery, TItem>(binding.Request!, context.RequestAborted);

                return (IResult)new CqrsStreamResult<TItem>(items);
            }
            catch(Exception exception) {
                return CqrsEndpointResults.MapException(context, exception);
            }
        }).Produces<IAsyncEnumerable<TItem>>();
    }
}