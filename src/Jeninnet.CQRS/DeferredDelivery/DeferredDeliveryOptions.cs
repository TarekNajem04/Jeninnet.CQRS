//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.DeferredDelivery;

/// <summary>
/// Options for deferred command delivery.
/// </summary>
public sealed class DeferredDeliveryOptions {
    /// <summary>
    /// Gets or sets how long staged entries live after deferral.
    /// Defaults to 30 days; set to <see langword="null"/> for no expiry.
    /// Expired entries never run and are pruned instead.
    /// </summary>
    public TimeSpan? TimeToLive { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Gets or sets whether pending entries are replayed at startup.
    /// Defaults to <see langword="true"/> (no-op for stores without cross-restart state).
    /// </summary>
    public bool EnableRecovery { get; set; } = true;

    /// <summary>
    /// Gets or sets how long shutdown waits for the in-flight deferred command to finish.
    /// Defaults to 10 seconds. When the deadline expires the execution token is cancelled and
    /// shutdown returns promptly; the staged entry remains for the next start (at-least-once).
    /// </summary>
    /// <remarks>
    /// Set <c>HostOptions.ShutdownTimeout</c> greater than this value so the host grants the pump
    /// its full drain window. New deferrals are rejected once shutdown begins.
    /// </remarks>
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
