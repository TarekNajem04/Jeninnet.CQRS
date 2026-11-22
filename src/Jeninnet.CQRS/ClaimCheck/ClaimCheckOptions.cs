//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.ClaimCheck;

/// <summary>
/// Options for claim-check storage.
/// </summary>
public sealed class ClaimCheckOptions {
    /// <summary>
    /// Gets or sets the size above which a payload is moved to the claim-check store, in bytes.
    /// Defaults to 64 KiB. Payloads at or below the threshold stay inline. Claim-check is only
    /// active when an <see cref="IClaimCheckStore"/> is registered.
    /// </summary>
    public int ThresholdBytes { get; set; } = 64 * 1024;
}