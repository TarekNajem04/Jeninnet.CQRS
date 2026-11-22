//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>
/// Encodes inline payload bytes into the string column without corrupting custom binary
/// serializers. Valid UTF-8 (the historical JSON shape) is stored as text; anything else is
/// Base64 behind a marker. Legacy rows without the marker keep decoding as UTF-8.
/// </summary>
internal static class PayloadText {
    private const string Base64Marker = "~b64~";

    internal static string Encode(ReadOnlySpan<byte> payload) {
        var text = Encoding.UTF8.GetString(payload);

        return Encoding.UTF8.GetBytes(text).AsSpan().SequenceEqual(payload)
            ? text
            : Base64Marker + Convert.ToBase64String(payload);
    }

    internal static byte[] Decode(string payload) =>
        payload.StartsWith(Base64Marker, StringComparison.Ordinal)
            ? Convert.FromBase64String(payload[Base64Marker.Length..])
            : Encoding.UTF8.GetBytes(payload);
}