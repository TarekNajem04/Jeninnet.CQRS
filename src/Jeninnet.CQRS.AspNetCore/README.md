# Jeninnet.CQRS.AspNetCore

Minimal-API endpoint mapping for the Jeninnet CQRS broker: one line per endpoint, with JSON
binding from the body/route/query, `ProblemDetails` for validation and unexpected failures, and
streaming `IAsyncEnumerable<T>` responses.

```csharp
builder.Services.AddCqrsEndpoints();

app.MapCqrsCommand<PlaceOrder, OrderId>("/orders", HttpMethods.Post);
app.MapCqrsCommand<CancelOrder>("/orders/{OrderId}", HttpMethods.Delete);
app.MapCqrsQuery<GetOrder, Order>("/orders/{OrderId}");
app.MapCqrsStream<StreamOrders, Order>("/orders/stream");
```

Commands bind a JSON body overlaid with route values (route wins) and query-string values, so a
bodyless `DELETE /orders/{id}` can bind `CancelOrder(string OrderId)`. Queries and streams bind
from route/query only. Every helper returns `RouteHandlerBuilder`, so
`RequireAuthorization`, `WithTags`, and other ASP.NET Core conventions compose normally.

- `ValidationException` becomes a 400 `ValidationProblemDetails` keyed by member name.
- Other exceptions become a 500 problem without leaking the message.
- Configure `CqrsEndpointOptions.ExceptionHandler` to map exceptions yourself.
- Streaming writes a JSON array incrementally; failures after the response starts propagate to
  the server.
