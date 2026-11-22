//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Outbox;

/// <summary>
/// In-memory <see cref="IOutboxStore"/>: the default. Pending notifications live only as long
/// as the process; use the EF Core store for restart safety. Singleton-safe. Leases, attempts,
/// and retry times are kept in memory with the same semantics as the durable store.
/// </summary>
public sealed class InMemoryOutboxStore(TimeProvider? clock = null, ITenantContext? tenant = null) : IOutboxStore {
    private readonly Lock _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ITenantContext? _tenant = tenant;

    /// <inheritdoc />
    public Task<string> StageAsync(IDeferredEnvelope notification, CancellationToken cancellationToken = default) {
        Guard.NotNull(notification);

        var key = OutboxKey.New(_clock.GetUtcNow());

        lock(_sync) {
            _entries[key] = new Entry(notification);
        }

        return Task.FromResult(key);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<(string Key, IDeferredEnvelope Notification)>> ListAsync(CancellationToken cancellationToken = default) {
        lock(_sync) {
            var current = _tenant?.Current;
            var listed = _entries
                .Where(pair => StringCompat.IsNullOrEmpty(current) || pair.Value.Envelope.TenantId == current)
                .OrderBy(static pair => pair.Value.Envelope.DueUtc)
                .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => (pair.Key, pair.Value.Envelope))
                .ToList();

            return Task.FromResult<IReadOnlyList<(string Key, IDeferredEnvelope Notification)>>(listed);
        }
    }

    /// <inheritdoc />
    public Task CompleteAsync(string key, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);

        lock(_sync) {
            _entries.Remove(key);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxLease>> LeaseAsync(
        string owner,
        int batchSize,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) {
        ValidateLease(owner, batchSize, leaseDuration);

        lock(_sync) {
            var leased = new List<OutboxLease>(Math.Min(batchSize, _entries.Count));
            var expiresAtUtc = now + leaseDuration;
            var current = _tenant?.Current;

            foreach(var pair in _entries
                .Where(pair => StringCompat.IsNullOrEmpty(current) || pair.Value.Envelope.TenantId == current)
                .OrderBy(static pair => pair.Value.Envelope.DueUtc)
                .ThenBy(static pair => pair.Key, StringComparer.Ordinal)) {
                if(leased.Count >= batchSize) {
                    break;
                }

                var entry = pair.Value;

                if(entry.NextAttemptUtc is { } nextAttemptUtc && nextAttemptUtc > now) {
                    break;
                }

                if(entry.IsLeaseActive(now)) {
                    break;
                }

                entry.LeaseOwner = owner;
                entry.LeaseExpiresAtUtc = expiresAtUtc;
                leased.Add(new OutboxLease(pair.Key, entry.Envelope, entry.Attempts, expiresAtUtc));
            }

            return Task.FromResult<IReadOnlyList<OutboxLease>>(leased);
        }
    }

    /// <inheritdoc />
    public Task<bool> CompleteLeaseAsync(string key, string owner, DateTimeOffset now, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);
        Guard.NotNullOrWhiteSpace(owner);

        lock(_sync) {
            if(!_entries.TryGetValue(key, out var entry) || !entry.IsOwnedBy(owner, now)) {
                return Task.FromResult(false);
            }

            _entries.Remove(key);

            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> FailLeaseAsync(
        string key,
        string owner,
        Exception error,
        int attempts,
        DateTimeOffset nextAttemptUtc,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);
        Guard.NotNullOrWhiteSpace(owner);
        Guard.NotNull(error);

        lock(_sync) {
            if(!_entries.TryGetValue(key, out var entry) || !entry.IsOwnedBy(owner, now)) {
                return Task.FromResult(false);
            }

            entry.LeaseOwner = null;
            entry.LeaseExpiresAtUtc = null;
            entry.Attempts = attempts;
            entry.NextAttemptUtc = nextAttemptUtc;

            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> ReleaseLeaseAsync(string key, string owner, DateTimeOffset now, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);
        Guard.NotNullOrWhiteSpace(owner);

        lock(_sync) {
            if(!_entries.TryGetValue(key, out var entry) || !entry.IsOwnedBy(owner, now)) {
                return Task.FromResult(false);
            }

            entry.LeaseOwner = null;
            entry.LeaseExpiresAtUtc = null;

            return Task.FromResult(true);
        }
    }

    private static void ValidateLease(string owner, int batchSize, TimeSpan leaseDuration) {
        Guard.NotNullOrWhiteSpace(owner);

        if(batchSize < 1) {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be at least 1.");
        }

        if(leaseDuration <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), leaseDuration, "Lease duration must be greater than zero.");
        }
    }

    private sealed class Entry(IDeferredEnvelope envelope) {
        internal IDeferredEnvelope Envelope { get; } = envelope;

        internal string? LeaseOwner { get; set; }

        internal DateTimeOffset? LeaseExpiresAtUtc { get; set; }

        internal int Attempts { get; set; }

        internal DateTimeOffset? NextAttemptUtc { get; set; }

        internal bool IsLeaseActive(DateTimeOffset now) =>
            LeaseOwner is not null && LeaseExpiresAtUtc is { } expiresAtUtc && expiresAtUtc > now;

        internal bool IsOwnedBy(string owner, DateTimeOffset now) =>
            string.Equals(LeaseOwner, owner, StringComparison.Ordinal) &&
            LeaseExpiresAtUtc is { } expiresAtUtc &&
            expiresAtUtc > now;
    }
}