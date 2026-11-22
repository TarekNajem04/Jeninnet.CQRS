//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.AspNetCore;

/// <summary>
/// Binds a request for a CQRS endpoint: a JSON object body for commands, overlaid with route
/// values (which win) and query-string values. Simple values are converted to the target
/// property type before deserialization, so a bodyless POST such as
/// <c>/orders/{orderId}</c> can bind a record command.
/// </summary>
internal static class CqrsRequestBinder {
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    internal static async Task<BindingResult<TRequest>> BindAsync<TRequest>(HttpContext context, bool readBody, CancellationToken cancellationToken)
        where TRequest : class {
        var payload = new JsonObject();

        if(readBody) {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            if(!string.IsNullOrWhiteSpace(body)) {
                try {
                    if(JsonNode.Parse(body) is not JsonObject bodyObject) {
                        return new BindingResult<TRequest>(null, InvalidBody("The request body must be a JSON object."));
                    }

                    payload = bodyObject;
                }
                catch(JsonException) {
                    return new BindingResult<TRequest>(null, InvalidBody("The JSON request body is invalid."));
                }
            }
        }

        foreach(var (name, values) in context.Request.Query) {
            Apply(payload, typeof(TRequest), name, values.Select(static value => value ?? string.Empty).ToArray());
        }

        foreach(var (name, value) in context.Request.RouteValues) {
            if(value is not null) {
                Apply(payload, typeof(TRequest), name, [value.ToString() ?? string.Empty]);
            }
        }

        try {
            var request = payload.Deserialize<TRequest>(_json);

            return request is null
                ? new BindingResult<TRequest>(null, InvalidBody("The request could not be bound."))
                : new BindingResult<TRequest>(request, null);
        }
        catch(JsonException) {
            return new BindingResult<TRequest>(null, InvalidBody("The request could not be bound to the message."));
        }
        catch(NotSupportedException) {
            return new BindingResult<TRequest>(null, InvalidBody("The request could not be bound to the message."));
        }
    }

    private static IResult InvalidBody(string detail) =>
        Results.Problem(title: "Invalid request.", detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private static void Apply(JsonObject payload, Type requestType, string name, string[] values) {
        var property = requestType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        // Unknown route/query keys are ignored: only message properties are bindable.
        if(property is null || !property.CanWrite || values.Length is 0) {
            return;
        }

        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var isArray = property.PropertyType.IsArray;

        if(values.Length > 1 || isArray) {
            var elementType = isArray ? property.PropertyType.GetElementType() ?? targetType : targetType;
            var array = new JsonArray();

            foreach(var value in values) {
                array.Add(ToNode(value, elementType));
            }

            payload[property.Name] = array;

            return;
        }

        payload[property.Name] = ToNode(values[0], targetType);
    }

    private static JsonNode? ToNode(string value, Type type) { // NOSONAR: Complexity is inherent to this state machine/pipeline and is covered by tests.
        if(type == typeof(string)) {
            return JsonValue.Create(value);
        }

        if(type.IsEnum) {
            return Enum.TryParse(type, value, ignoreCase: true, out var parsed)
                ? JsonSerializer.SerializeToNode(parsed, type, _json)
                : JsonValue.Create(value);
        }

        if(type == typeof(bool) && bool.TryParse(value, out var boolean)) {
            return JsonValue.Create(boolean);
        }

        if(type == typeof(Guid) && Guid.TryParse(value, out var guid)) {
            return JsonValue.Create(guid);
        }

        if(type == typeof(DateTimeOffset) && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTimeOffset)) {
            return JsonValue.Create(dateTimeOffset);
        }

        if(type == typeof(DateTime) && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime)) {
            return JsonValue.Create(dateTime);
        }

        if(type == typeof(DateOnly) && DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly)) {
            return JsonValue.Create(dateOnly);
        }

        if(type == typeof(TimeOnly) && TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timeOnly)) {
            return JsonValue.Create(timeOnly);
        }

        if(type == typeof(TimeSpan) && TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var timeSpan)) {
            return JsonValue.Create(timeSpan);
        }

        try {
            var converted = Convert.ChangeType(value, type, CultureInfo.InvariantCulture);

            return JsonSerializer.SerializeToNode(converted, type, _json);
        }
        catch(FormatException) {
            return JsonValue.Create(value);
        }
        catch(InvalidCastException) {
            return JsonValue.Create(value);
        }
        catch(OverflowException) {
            return JsonValue.Create(value);
        }
    }

    internal readonly record struct BindingResult<TRequest>(TRequest? Request, IResult? Error)
        where TRequest : class;
}
