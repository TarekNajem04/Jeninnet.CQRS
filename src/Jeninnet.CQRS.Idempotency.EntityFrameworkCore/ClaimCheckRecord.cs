//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>
/// A captured payload kept outside the message row: content type plus raw bytes. Rows reference
/// it by key so large payloads do not bloat the durable tables or the WAL.
/// </summary>
public sealed class ClaimCheckRecord {
    /// <summary>Gets or sets the claim-check key. Primary key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the serializer content type that produced the payload.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Gets or sets the serialized payload bytes.</summary>
    public byte[] Payload { get; set; } = [];

    /// <summary>Gets or sets when the payload was stored, in UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }
}