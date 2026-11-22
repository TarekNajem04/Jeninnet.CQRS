//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Polyfills;

/// <summary>Span-based integer parsing with a netstandard2.0 fallback.</summary>
internal static class Int32Compat {
    /// <summary>Parses an integer from a character span.</summary>
    /// <param name="value">The characters to parse.</param>
    /// <param name="result">The parsed value.</param>
    /// <returns><see langword="true"/> when parsing succeeded.</returns>
    public static bool TryParse(ReadOnlySpan<char> value, out int result) {
#if NETSTANDARD2_0 || NET462
        return int.TryParse(value.ToString(), out result);
#else
        return int.TryParse(value, out result);
#endif
    }
}
