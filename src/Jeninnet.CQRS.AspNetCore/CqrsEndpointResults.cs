//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.AspNetCore;

/// <summary>
/// Maps endpoint exceptions to <c>ProblemDetails</c> responses. HTTP concerns stay here; the
/// core broker never learns about ASP.NET Core.
/// </summary>
internal static class CqrsEndpointResults {
    internal static IResult MapException(HttpContext context, Exception exception) {
        var options = context.RequestServices.GetService<IOptions<CqrsEndpointOptions>>()?.Value;

        if(options?.ExceptionHandler is { } handler) {
            try {
                return handler(context, exception);
            }
            catch(Exception) {
                return InternalError();
            }
        }

        if(exception is ValidationException validation) {
            var errors = validation.Errors
                .GroupBy(static error => string.IsNullOrWhiteSpace(error.MemberName) ? "request" : error.MemberName, StringComparer.Ordinal)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.Select(static error => error.Message ?? error.MessageId.Id).ToArray(),
                    StringComparer.Ordinal);

            return Results.ValidationProblem(errors, title: "One or more validation errors occurred.", statusCode: StatusCodes.Status400BadRequest);
        }

        return InternalError();
    }

    private static IResult InternalError() =>
        Results.Problem(title: "An unexpected error occurred.", statusCode: StatusCodes.Status500InternalServerError);
}