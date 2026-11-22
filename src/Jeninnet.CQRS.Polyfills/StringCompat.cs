//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Polyfills;

/// <summary>
/// Null/whitespace helpers with the nullable annotations netstandard2.0's BCL method lacks.
/// </summary>
internal static class StringCompat {
    /// <summary>Returns whether a string is null or whitespace.</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> when the value is null or whitespace.</returns>
    public static bool IsNullOrWhiteSpace([System.Diagnostics.CodeAnalysis.NotNullWhen(false)] string? value) =>
        string.IsNullOrWhiteSpace(value);

    /// <summary>Returns whether a string is null or empty.</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> when the value is null or empty.</returns>
    public static bool IsNullOrEmpty([System.Diagnostics.CodeAnalysis.NotNullWhen(false)] string? value) =>
        string.IsNullOrEmpty(value);
}
