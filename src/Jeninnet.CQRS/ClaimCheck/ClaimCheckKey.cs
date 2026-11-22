//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.ClaimCheck;

/// <summary>
/// Computes the content-addressed key used by the shipped claim-check providers. The key is the
/// lowercase SHA-256 of the UTF-8 content type, a NUL separator, and the payload, so equal bytes
/// with equal metadata deduplicate while the same bytes under a different serializer stay
/// distinct.
/// </summary>
public static class ClaimCheckKey {
    /// <summary>Computes the deterministic key for a payload and its content type.</summary>
    /// <param name="payload">The serialized bytes.</param>
    /// <param name="contentType">The serializer content type.</param>
    /// <returns>A 64-character lowercase hexadecimal key.</returns>
    public static string Compute(ReadOnlyMemory<byte> payload, string contentType) {
        Guard.NotNullOrWhiteSpace(contentType);

        var typeBytes = Encoding.UTF8.GetBytes(contentType);
        var data = new byte[typeBytes.Length + 1 + payload.Length];

        typeBytes.CopyTo(data, 0);
        data[typeBytes.Length] = 0;
        payload.Span.CopyTo(data.AsSpan(typeBytes.Length + 1));

#if NETSTANDARD2_0 || NET462
        using var algorithm = System.Security.Cryptography.SHA256.Create();
        var hash = algorithm.ComputeHash(data);

        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
#else
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(data));
#endif
    }
}
