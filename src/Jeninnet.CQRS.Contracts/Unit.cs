//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS;

/// <summary>
/// A placeholder response type used when a command handler does not return a value.
/// </summary>
/// <remarks>
/// Use this type to unify void-return commands with the pipeline.
/// </remarks>
public readonly record struct Unit {
    /// <summary>
    /// The single <see cref="Unit"/> instance.
    /// </summary>
    public static readonly Unit Value;
}
