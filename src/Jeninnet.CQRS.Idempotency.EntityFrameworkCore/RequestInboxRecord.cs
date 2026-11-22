//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>
/// One pending request row. Response delivery is process-local; the row gives the inbox a TTL and
/// operator visibility, and is removed when the client completes or times out.
/// </summary>
public sealed class RequestInboxRecord : ITenantOwned {
    /// <summary>Gets or sets the tenant id.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the request/correlation id. Primary key.</summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>Gets or sets the request type name.</summary>
    public string RequestType { get; set; } = string.Empty;

    /// <summary>Gets or sets the response type name.</summary>
    public string ResponseType { get; set; } = string.Empty;

    /// <summary>Gets or sets the response mode (0 first, 1 all).</summary>
    public int Mode { get; set; }

    /// <summary>Gets or sets when the request was registered.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Gets or sets when the pending row expires.</summary>
    public DateTimeOffset ExpiresAtUtc { get; set; }
}
