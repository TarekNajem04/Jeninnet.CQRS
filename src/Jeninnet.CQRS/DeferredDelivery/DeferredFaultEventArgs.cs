//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.DeferredDelivery;

/// <summary>
/// Describes a deferred command that failed instead of completing.
/// </summary>
/// <param name="CommandTypeName">The full name of the deferred command type.</param>
/// <param name="Error">The exception that escaped the handler.</param>
/// <param name="OccurredAtUtc">When the failure was observed.</param>
public sealed class DeferredFaultEventArgs(string CommandTypeName, Exception Error, DateTimeOffset OccurredAtUtc) : EventArgs {
    /// <summary>Gets the full name of the deferred command type.</summary>
    public string CommandTypeName { get; } = CommandTypeName;

    /// <summary>Gets the exception that escaped the handler.</summary>
    public Exception Error { get; } = Error;

    /// <summary>Gets when the failure was observed.</summary>
    public DateTimeOffset OccurredAtUtc { get; } = OccurredAtUtc;
}