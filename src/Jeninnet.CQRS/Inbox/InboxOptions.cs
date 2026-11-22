//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Inbox;

/// <summary>
/// Inbox retention and completion policy.
/// </summary>
public sealed class InboxOptions {
    /// <summary>Gets or sets whether inbox deduplication is active. Defaults to <see langword="true"/>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets how long a processed row is retained for duplicate detection. Defaults to
    /// seven days. A redelivery older than this window runs again.
    /// </summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Gets or sets how long a claimed but unfinished row blocks a redelivery. Defaults to five
    /// minutes. A consumer crash beyond this window makes the message reclaimable (the documented
    /// at-least-once gap).
    /// </summary>
    public TimeSpan PendingTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets how often expired rows are pruned. Defaults to one hour; zero disables the
    /// background pruner.
    /// </summary>
    public TimeSpan PruneInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets whether the middleware completes the inbox row after the handler succeeds.
    /// Defaults to <see langword="true"/>. Set it to <see langword="false"/> when the handler
    /// completes the row inside its own database transaction (the mitigation for the crash window
    /// between handler success and inbox completion).
    /// </summary>
    public bool AutoComplete { get; set; } = true;

    /// <summary>
    /// Gets or sets whether completion stores a SHA-256 of the inbound payload. Defaults to
    /// <see langword="true"/>; the hash is audit metadata and is never used for deduplication.
    /// </summary>
    public bool ComputePayloadHash { get; set; } = true;

    internal void Validate() {
        if(!Enabled) {
            return;
        }

        if(PendingTimeout <= TimeSpan.Zero || PendingTimeout > TimeSpan.FromDays(1)) {
            throw new InvalidOperationException("Inbox PendingTimeout must be greater than zero and at most one day.");
        }

        if(Retention <= TimeSpan.Zero) {
            throw new InvalidOperationException("Inbox Retention must be greater than zero.");
        }

        if(PruneInterval < TimeSpan.Zero) {
            throw new InvalidOperationException("Inbox PruneInterval must be zero or greater.");
        }
    }
}
