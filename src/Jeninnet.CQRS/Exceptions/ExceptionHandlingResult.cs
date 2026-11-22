//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Exceptions;

/// <summary>
/// Outcome of a typed exception handler: either a replacement response, or "not mine" so
/// resolution continues with the next (less specific) handler.
/// </summary>
/// <typeparam name="TResponse">The pipeline response type.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Static factories are the intended API shape for this generic result type.")]
public readonly record struct ExceptionHandlingResult<TResponse> {
    private ExceptionHandlingResult(bool isHandled, TResponse? response) {
        IsHandled = isHandled;
        Response = response;
    }

    /// <summary>Gets a value indicating whether the handler produced a response.</summary>
    public bool IsHandled { get; }

    /// <summary>Gets the replacement response when <see cref="IsHandled"/> is <see langword="true"/>.</summary>
    public TResponse? Response { get; }

    /// <summary>Creates a handled result.</summary>
    /// <param name="response">The response the pipeline should return.</param>
    public static ExceptionHandlingResult<TResponse> Handled(TResponse response) => new(true, response);

    /// <summary>Gets the "not handled, keep looking" result.</summary>
    public static ExceptionHandlingResult<TResponse> Unhandled => default;
}