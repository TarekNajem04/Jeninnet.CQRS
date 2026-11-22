//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.RequestResponse;

/// <summary>Options for request/response clients and the pending inbox.</summary>
public sealed class RequestResponseOptions {
    /// <summary>
    /// Gets or sets the timeout used by the no-timeout client overloads. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how often expired pending-request rows are pruned. Defaults to one minute;
    /// non-positive values disable the pruning service.
    /// </summary>
    public TimeSpan InboxPruneInterval { get; set; } = TimeSpan.FromMinutes(1);
}