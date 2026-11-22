//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Polyfills;

/// <summary>Hash helpers with netstandard2.0-compatible implementations.</summary>
internal static class CryptoCompat {
    /// <summary>Computes a SHA-256 hash.</summary>
    /// <param name="value">The bytes to hash.</param>
    /// <returns>The hash bytes.</returns>
    public static byte[] Sha256(byte[] value) {
#if NETSTANDARD2_0 || NET462
        using var algorithm = System.Security.Cryptography.SHA256.Create();

        return algorithm.ComputeHash(value);
#else
        return System.Security.Cryptography.SHA256.HashData(value);
#endif
    }

    /// <summary>Formats bytes as uppercase hexadecimal.</summary>
    /// <param name="value">The bytes to format.</param>
    /// <returns>The hexadecimal string.</returns>
    public static string ToHex(byte[] value) {
#if NETSTANDARD2_0 || NET462
        return System.BitConverter.ToString(value).Replace("-", string.Empty);
#else
        return Convert.ToHexString(value);
#endif
    }
}
