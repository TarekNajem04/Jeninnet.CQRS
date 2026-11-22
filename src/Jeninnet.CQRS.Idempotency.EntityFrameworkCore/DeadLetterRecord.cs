//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>
/// A dead-lettered command: JSON payload plus the assembly-qualified command type needed to
/// rebuild the envelope for replay, with attempt and last-error bookkeeping.
/// </summary>
public sealed class DeadLetterRecord : ITenantOwned {
    /// <summary>Gets or sets the tenant id.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the store key. Primary key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the assembly-qualified command type name.</summary>
    public string CommandType { get; set; } = string.Empty;

    /// <summary>Gets or sets the JSON payload of the command.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the claim-check key when the payload lives outside the row.
    /// <see langword="null"/> means <see cref="Payload"/> is inline (both shapes are readable).
    /// </summary>
    public string? ClaimCheckKey { get; set; }

    /// <summary>Gets or sets the original due time, in UTC.</summary>
    public DateTimeOffset DueUtc { get; set; }

    /// <summary>Gets or sets the original expiry, in UTC. <see langword="null"/> never expires.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    /// <summary>Gets or sets how many executions have failed.</summary>
    public int Attempts { get; set; }

    /// <summary>Gets or sets the full name of the last exception type.</summary>
    public string LastErrorType { get; set; } = string.Empty;

    /// <summary>Gets or sets the message of the last exception.</summary>
    public string LastErrorMessage { get; set; } = string.Empty;

    /// <summary>Gets or sets when the first failure was observed, in UTC.</summary>
    public DateTimeOffset FirstFailedAtUtc { get; set; }

    /// <summary>Gets or sets when the most recent failure was observed, in UTC.</summary>
    public DateTimeOffset LastFailedAtUtc { get; set; }
}
