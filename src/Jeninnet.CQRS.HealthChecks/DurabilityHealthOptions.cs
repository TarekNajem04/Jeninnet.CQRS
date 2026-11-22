//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.HealthChecks;

/// <summary>
/// Thresholds for the durability health checks.
/// </summary>
public sealed class DurabilityHealthOptions {
    /// <summary>
    /// Gets or sets the deferred backlog at which health becomes
    /// <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded"/>.
    /// Defaults to 100 pending commands.
    /// </summary>
    public int DeferredDegradedBacklog { get; set; } = 100;

    /// <summary>
    /// Gets or sets the deferred backlog at which health becomes
    /// <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy"/>.
    /// Defaults to 1000 pending commands; must be at least the degraded threshold.
    /// </summary>
    public int DeferredUnhealthyBacklog { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the outbox lag at which health becomes
    /// <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded"/>.
    /// Defaults to one minute.
    /// </summary>
    public TimeSpan OutboxDegradedLag { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the outbox lag at which health becomes
    /// <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy"/>.
    /// Defaults to ten minutes; must be at least the degraded lag.
    /// </summary>
    public TimeSpan OutboxUnhealthyLag { get; set; } = TimeSpan.FromMinutes(10);
}
