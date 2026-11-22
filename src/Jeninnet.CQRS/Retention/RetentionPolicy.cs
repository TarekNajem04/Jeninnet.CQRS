//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Retention;

/// <summary>What the sweeper does with an eligible active row.</summary>
public enum RetentionAction {
    /// <summary>Copy the row to the archive table, then remove it from the active table.</summary>
    Archive = 0,

    /// <summary>Delete the row without an archive copy.</summary>
    Delete = 1,
}

/// <summary>
/// Retention settings for one store. Nothing is deleted or archived unless a policy is registered
/// for that store; the sweeper's default is dry-run.
/// </summary>
public sealed record RetentionPolicy {
    /// <summary>Gets whether this policy participates in sweeps.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Gets whether eligible rows are archived or deleted.</summary>
    public RetentionAction Action { get; init; } = RetentionAction.Archive;

    /// <summary>Gets how long a completed row is kept before it becomes eligible.</summary>
    public TimeSpan Retention { get; init; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Gets an additional grace period. It is applied by stores that need a safety window (the
    /// dead-letter store); other stores ignore it.
    /// </summary>
    public TimeSpan GracePeriod { get; init; } = TimeSpan.Zero;

    internal void Validate(string store) {
        if(Retention <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(store, Retention, $"Retention for store '{store}' must be greater than zero.");
        }

        if(GracePeriod < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(store, GracePeriod, $"GracePeriod for store '{store}' must not be negative.");
        }
    }
}

/// <summary>Well-known names for the stores shipped by the EF Core packages.</summary>
public static class RetentionStoreNames {
    /// <summary>The completed notification outbox.</summary>
    public const string Outbox = "outbox";

    /// <summary>The consumer inbox.</summary>
    public const string Inbox = "inbox";

    /// <summary>The dead-letter table.</summary>
    public const string DeadLetters = "dead-letters";

    /// <summary>Terminal background jobs.</summary>
    public const string Jobs = "jobs";

    /// <summary>Terminal sagas.</summary>
    public const string Sagas = "sagas";
}

/// <summary>One store's eligible-row count and the action the sweeper took (or would take).</summary>
/// <param name="Store">The store name.</param>
/// <param name="Candidates">Rows considered in this batch.</param>
/// <param name="Archived">Rows copied to the archive (or that would be in a dry run).</param>
/// <param name="Deleted">Rows deleted (or that would be in a dry run).</param>
/// <param name="DryRun">Whether the sweep was a dry run.</param>
public sealed record RetentionBatchResult(string Store, int Candidates, int Archived, int Deleted, bool DryRun) {
    /// <summary>Gets the number of rows changed (or that would change).</summary>
    public int Affected => Archived + Deleted;
}

/// <summary>
/// One store's retention adapter. A store decides which rows are completed/terminal and applies
/// the policy's cutoff; the sweeper never inspects a store's schema.
/// </summary>
public interface IRetentionStore {
    /// <summary>Gets the stable store name used to select a policy and metric tag.</summary>
    string Name { get; }

    /// <summary>
    /// Archives or deletes up to <paramref name="batchSize"/> eligible rows. A dry run counts the
    /// same rows without changing storage.
    /// </summary>
    /// <param name="policy">The policy for this store.</param>
    /// <param name="now">The current UTC time from the sweeper clock.</param>
    /// <param name="batchSize">The maximum rows to process.</param>
    /// <param name="dryRun">When true, no row is written or deleted.</param>
    /// <param name="cancellationToken">Cancels the sweep.</param>
    Task<RetentionBatchResult> SweepAsync(
        RetentionPolicy policy,
        DateTimeOffset now,
        int batchSize,
        bool dryRun,
        CancellationToken cancellationToken = default);
}

/// <summary>Configuration for the hosted retention sweeper.</summary>
public sealed class RetentionOptions {
    /// <summary>Gets or sets whether the hosted sweeper runs. Defaults to <see langword="true"/>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the sweep interval. Defaults to one hour; zero disables the timer.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Gets or sets the maximum rows processed per store per sweep. Defaults to 100.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Gets or sets the distributed lease duration. Defaults to five minutes.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets whether a sweep reports counts without changing storage. Defaults to
    /// <see langword="true"/>; opt in explicitly before enabling destructive retention.
    /// </summary>
    public bool DryRun { get; set; } = true;

    /// <summary>Gets the configured policies keyed by <see cref="RetentionStoreNames"/>.</summary>
    public IDictionary<string, RetentionPolicy> Policies { get; } = new Dictionary<string, RetentionPolicy>(StringComparer.Ordinal);

    /// <summary>Adds or replaces a policy for a store.</summary>
    /// <param name="store">The store name, usually a <see cref="RetentionStoreNames"/> constant.</param>
    /// <param name="policy">The policy.</param>
    /// <returns>This options instance for chaining.</returns>
    public RetentionOptions AddPolicy(string store, RetentionPolicy policy) {
        Guard.NotNullOrWhiteSpace(store);
        Guard.NotNull(policy);

        Policies[store] = policy;

        return this;
    }

    internal void Validate() {
        if(Interval < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(Interval), Interval, "The retention interval must not be negative."); // NOSONAR: The property name is the public option key used in the validation error.
        }

        if(BatchSize < 1) {
            throw new ArgumentOutOfRangeException(nameof(BatchSize), BatchSize, "The retention batch size must be at least 1."); // NOSONAR: The property name is the public option key used in the validation error.
        }

        if(LeaseDuration <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(LeaseDuration), LeaseDuration, "The retention lease duration must be greater than zero."); // NOSONAR: The property name is the public option key used in the validation error.
        }

        foreach(var pair in Policies) {
            pair.Value.Validate(pair.Key);
        }
    }
}

/// <summary>
/// A cross-process lease for the sweeper. The default in-memory implementation protects one host;
/// replace it with a durable implementation when several instances share one database.
/// </summary>
public interface IRetentionLease {
    /// <summary>Tries to acquire or extend the sweep lease.</summary>
    /// <param name="owner">The caller's owner id.</param>
    /// <param name="duration">How long the lease lasts.</param>
    /// <param name="now">The current UTC time.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    Task<bool> TryAcquireAsync(string owner, TimeSpan duration, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Releases the lease when the caller still owns it.</summary>
    /// <param name="owner">The caller's owner id.</param>
    /// <param name="cancellationToken">Cancels the release.</param>
    Task ReleaseAsync(string owner, CancellationToken cancellationToken = default);
}

/// <summary>Process-local retention lease with expiry; safe for concurrent hosted services.</summary>
public sealed class InMemoryRetentionLease : IRetentionLease {
    private readonly Lock _sync = new();
    private string? _owner;
    private DateTimeOffset _expiresAtUtc;

    /// <inheritdoc />
    public Task<bool> TryAcquireAsync(string owner, TimeSpan duration, DateTimeOffset now, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(owner);

        if(duration <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Lease duration must be greater than zero.");
        }

        lock(_sync) {
            if(_owner is null || _owner == owner || _expiresAtUtc <= now) {
                _owner = owner;
                _expiresAtUtc = now + duration;

                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
    }

    /// <inheritdoc />
    public Task ReleaseAsync(string owner, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(owner);

        lock(_sync) {
            if(_owner == owner) {
                _owner = null;
                _expiresAtUtc = default;
            }
        }

        return Task.CompletedTask;
    }
}