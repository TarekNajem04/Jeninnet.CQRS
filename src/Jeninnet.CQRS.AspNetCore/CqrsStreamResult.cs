//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.AspNetCore;

/// <summary>
/// Streams an <see cref="IAsyncEnumerable{T}"/> as a JSON array without buffering the sequence.
/// Exceptions raised during enumeration happen after the response has started and propagate to
/// the server; binding and handler-resolution failures are mapped before the result runs.
/// </summary>
/// <typeparam name="TItem">The streamed item type.</typeparam>
/// <param name="items">The sequence to stream. Never <see langword="null"/>.</param>
internal sealed class CqrsStreamResult<TItem>(IAsyncEnumerable<TItem> items) : IResult {
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web); // NOSONAR: Static field is intentionally scoped per closed generic type.

    private readonly IAsyncEnumerable<TItem> _items = items ?? throw new ArgumentNullException(nameof(items));

    /// <inheritdoc />
    public async Task ExecuteAsync(HttpContext httpContext) {
        ArgumentNullException.ThrowIfNull(httpContext);

        var cancellationToken = httpContext.RequestAborted;
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await httpContext.Response.WriteAsync("[", cancellationToken).ConfigureAwait(false);

        var first = true;

        await foreach(var item in _items.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            if(!first) {
                await httpContext.Response.WriteAsync(",", cancellationToken).ConfigureAwait(false);
            }

            first = false;
            await JsonSerializer.SerializeAsync(httpContext.Response.Body, item, _json, cancellationToken).ConfigureAwait(false);
        }

        await httpContext.Response.WriteAsync("]", cancellationToken).ConfigureAwait(false);
    }
}