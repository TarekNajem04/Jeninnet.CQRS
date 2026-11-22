//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Polyfills;

/// <summary>UTF-8 string decoding with a netstandard2.0 fallback.</summary>
internal static class EncodingCompat {
    /// <summary>Decodes UTF-8 bytes.</summary>
    /// <param name="value">The bytes to decode.</param>
    /// <returns>The decoded string.</returns>
    public static string GetUtf8String(ReadOnlySpan<byte> value) {
#if NETSTANDARD2_0 || NET462
        return System.Text.Encoding.UTF8.GetString(value.ToArray());
#else
        return System.Text.Encoding.UTF8.GetString(value);
#endif
    }
}