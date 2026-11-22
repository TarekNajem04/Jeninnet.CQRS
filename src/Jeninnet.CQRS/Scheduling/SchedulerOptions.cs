//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Scheduling;

/// <summary>Options for the persistent scheduler worker.</summary>
public sealed class SchedulerOptions {
    /// <summary>Gets or sets how often the worker scans for due rows. Defaults to one second.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets the due-scan batch size. Defaults to 32.</summary>
    public int BatchSize { get; set; } = 32;

    /// <summary>Gets or sets the lease duration. Defaults to 30 seconds.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the first retry delay after a failed publish. Defaults to one second.</summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets the retry backoff ceiling. Defaults to five minutes.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets or sets the exponential backoff multiplier. Defaults to 2.</summary>
    public double BackoffMultiplier { get; set; } = 2;

    /// <summary>
    /// Gets or sets the worker owner id. <see langword="null"/> generates a process-unique id so
    /// two instances cannot claim each other's leases.
    /// </summary>
    public string? OwnerId { get; set; }
}