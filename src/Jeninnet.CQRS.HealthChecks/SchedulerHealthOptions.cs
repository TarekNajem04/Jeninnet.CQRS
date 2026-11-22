//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.HealthChecks;

/// <summary>Due-backlog thresholds for the scheduler health check.</summary>
public sealed class SchedulerHealthOptions {
    /// <summary>Gets or sets the due count at which health becomes degraded. Defaults to 100.</summary>
    public int DueDegraded { get; set; } = 100;

    /// <summary>Gets or sets the due count at which health becomes unhealthy. Defaults to 1000.</summary>
    public int DueUnhealthy { get; set; } = 1000;
}