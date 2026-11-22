//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Jobs;

/// <summary>Options for the job queue, worker, retry policy, cancellation watcher, and retention.</summary>
public sealed class JobOptions {
    /// <summary>Gets or sets how often the worker scans for due jobs. Defaults to one second.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets the due-scan batch size. Defaults to 16.</summary>
    public int BatchSize { get; set; } = 16;

    /// <summary>Gets or sets the execution lease duration. Defaults to 30 seconds.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets how often the worker checks the durable cancellation flag. Defaults to one second.</summary>
    public TimeSpan CancellationPollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets the attempt budget captured on submit. Defaults to 3.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Gets or sets the delay before the second attempt. Defaults to one second.</summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets the exponential backoff ceiling. Defaults to five minutes.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets or sets the exponential backoff multiplier. Defaults to 2.</summary>
    public double BackoffMultiplier { get; set; } = 2;

    /// <summary>Gets or sets how long a terminal job is retained. Defaults to seven days.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Gets or sets how often terminal jobs are pruned. Defaults to one hour; zero disables the pruner.</summary>
    public TimeSpan PruneInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the worker owner id. <see langword="null"/> generates a process-unique id so
    /// two workers cannot claim each other's leases.
    /// </summary>
    public string? OwnerId { get; set; }

    internal void Validate() {
        if(PollInterval <= TimeSpan.Zero) {
            throw new InvalidOperationException("Job PollInterval must be greater than zero.");
        }

        if(BatchSize < 1) {
            throw new InvalidOperationException("Job BatchSize must be at least 1.");
        }

        if(LeaseDuration <= TimeSpan.Zero) {
            throw new InvalidOperationException("Job LeaseDuration must be greater than zero.");
        }

        if(CancellationPollInterval <= TimeSpan.Zero) {
            throw new InvalidOperationException("Job CancellationPollInterval must be greater than zero.");
        }

        if(MaxAttempts < 1) {
            throw new InvalidOperationException("Job MaxAttempts must be at least 1.");
        }

        if(InitialRetryDelay < TimeSpan.Zero || MaxRetryDelay < TimeSpan.Zero) {
            throw new InvalidOperationException("Job retry delays must not be negative.");
        }

        if(MaxRetryDelay < InitialRetryDelay) {
            throw new InvalidOperationException("Job MaxRetryDelay must be at least InitialRetryDelay.");
        }

        if(double.IsNaN(BackoffMultiplier) || double.IsInfinity(BackoffMultiplier) || BackoffMultiplier < 1) {
            throw new InvalidOperationException("Job BackoffMultiplier must be a finite value at least 1.");
        }

        if(Retention <= TimeSpan.Zero) {
            throw new InvalidOperationException("Job Retention must be greater than zero.");
        }

        if(PruneInterval < TimeSpan.Zero) {
            throw new InvalidOperationException("Job PruneInterval must be zero or greater.");
        }
    }
}
