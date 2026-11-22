//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.AspNetCore;

/// <summary>
/// Options for the minimal-API endpoint helpers.
/// </summary>
public sealed class CqrsEndpointOptions {
    /// <summary>
    /// Gets or sets a custom mapping from an endpoint exception to an <see cref="IResult"/>.
    /// Defaults to <see langword="null"/>, which maps <see cref="ValidationException"/> to a
    /// 400 validation problem and everything else to a 500 problem without leaking the message.
    /// </summary>
    public Func<HttpContext, Exception, IResult>? ExceptionHandler { get; set; }
}
