//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.HealthChecks;

/// <summary>
/// Thresholds for the routing-slip health contribution.
/// </summary>
public sealed class RoutingSlipHealthOptions {
    /// <summary>
    /// Gets or sets the age at which a running or compensating slip is considered stuck.
    /// Defaults to ten minutes; terminal slips never count.
    /// </summary>
    public TimeSpan StuckAfter { get; set; } = TimeSpan.FromMinutes(10);
}
