//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Scheduling;

/// <summary>In-memory scheduler store. Rows live only as long as the process; use the EF store for restart safety.</summary>
/// <param name="tenant">The ambient tenant; scopes caller-supplied schedule keys per tenant.</param>
public sealed class InMemorySchedulerStore(ITenantContext? tenant = null) : ISchedulerStore {
    private readonly Lock _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ITenantContext? _tenant = tenant;

    /// <inheritdoc />
    public Task<bool> TryAddAsync(ScheduledMessage message, CancellationToken cancellationToken = default) {
        Guard.NotNull(message);

        lock(_sync) {
            var key = ScopedKey(message.Key);

            if(_entries.ContainsKey(key)) {
                return Task.FromResult(false);
            }

            _entries.Add(key, new Entry(message));

            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SchedulerLease>> LeaseDueAsync(string owner, int batchSize, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken = default) {
        ValidateLease(owner, batchSize, leaseDuration);

        lock(_sync) {
            var leases = new List<SchedulerLease>(Math.Min(batchSize, _entries.Count));
            var expiresAtUtc = now + leaseDuration;
            var current = _tenant?.Current;

            foreach(var pair in _entries // NOSONAR: Explicit loop is clearer and faster than a LINQ pipeline here.
                .Where(pair => StringCompat.IsNullOrEmpty(current) || pair.Value.Message.TenantId == current)
                .OrderBy(static pair => pair.Value.Message.NextRunUtc)
                .ThenBy(static pair => pair.Key, StringComparer.Ordinal)) {
                if(leases.Count >= batchSize) {
                    break;
                }

                var entry = pair.Value;

                if(entry.Message.NextRunUtc > now) {
                    continue;
                }

                if(entry.LeaseOwner is not null && entry.LeaseExpiresAtUtc is { } currentExpiry && currentExpiry > now) {
                    continue;
                }

                var leased = entry.Message with { Version = entry.Message.Version + 1 };
                entry.Message = leased;
                entry.LeaseOwner = owner;
                entry.LeaseExpiresAtUtc = expiresAtUtc;
                leases.Add(new SchedulerLease(leased, owner, expiresAtUtc));
            }

            return Task.FromResult<IReadOnlyList<SchedulerLease>>(leases);
        }
    }

    /// <inheritdoc />
    public Task<bool> CompleteAsync(string key, string owner, long version, CancellationToken cancellationToken = default) {
        ValidateKeyOwner(key, owner);

        lock(_sync) {
            if(!_entries.TryGetValue(ScopedKey(key), out var entry) || !entry.IsOwned(owner, version)) {
                return Task.FromResult(false);
            }

            _entries.Remove(ScopedKey(key));

            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> ScheduleNextAsync(string key, string owner, long version, DateTimeOffset nextRunUtc, CancellationToken cancellationToken = default) {
        ValidateKeyOwner(key, owner);

        lock(_sync) {
            if(!_entries.TryGetValue(ScopedKey(key), out var entry) || !entry.IsOwned(owner, version)) {
                return Task.FromResult(false);
            }

            entry.Message = entry.Message with {
                NextRunUtc = nextRunUtc,
                Attempts = 0,
                Version = version + 1,
            };
            entry.LeaseOwner = null;
            entry.LeaseExpiresAtUtc = null;

            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> FailAsync(string key, string owner, long version, Exception error, int attempts, DateTimeOffset nextRunUtc, CancellationToken cancellationToken = default) {
        ValidateKeyOwner(key, owner);
        Guard.NotNull(error);

        lock(_sync) {
            if(!_entries.TryGetValue(ScopedKey(key), out var entry) || !entry.IsOwned(owner, version)) {
                return Task.FromResult(false);
            }

            entry.Message = entry.Message with {
                NextRunUtc = nextRunUtc,
                Attempts = attempts,
                Version = version + 1,
            };
            entry.LeaseOwner = null;
            entry.LeaseExpiresAtUtc = null;

            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> CancelAsync(string key, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);

        lock(_sync) {
            return Task.FromResult(_entries.Remove(ScopedKey(key)));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScheduledMessage>> ListAsync(CancellationToken cancellationToken = default) {
        lock(_sync) {
            var current = _tenant?.Current;
            var listed = _entries.Values
                .Where(entry => StringCompat.IsNullOrEmpty(current) || entry.Message.TenantId == current)
                .Select(static entry => entry.Message)
                .OrderBy(static message => message.NextRunUtc)
                .ThenBy(static message => message.Key, StringComparer.Ordinal)
                .ToList();

            return Task.FromResult<IReadOnlyList<ScheduledMessage>>(listed);
        }
    }

    /// <inheritdoc />
    public Task<long> CountDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default) {
        lock(_sync) {
            var current = _tenant?.Current;

            return Task.FromResult(_entries.Values.LongCount(entry =>
                (StringCompat.IsNullOrEmpty(current) || entry.Message.TenantId == current) &&
                entry.Message.NextRunUtc <= now));
        }
    }

    private string ScopedKey(string key) =>
        _tenant?.Current is { Length: > 0 } currentTenant ? $"{currentTenant}:{key}" : key;

    private static void ValidateLease(string owner, int batchSize, TimeSpan leaseDuration) {
        Guard.NotNullOrWhiteSpace(owner);

        if(batchSize < 1) {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be at least 1.");
        }

        if(leaseDuration <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), leaseDuration, "Lease duration must be greater than zero.");
        }
    }

    private static void ValidateKeyOwner(string key, string owner) {
        Guard.NotNullOrWhiteSpace(key);
        Guard.NotNullOrWhiteSpace(owner);
    }

    private sealed class Entry(ScheduledMessage message) {
        internal ScheduledMessage Message { get; set; } = message;

        internal string? LeaseOwner { get; set; }

        internal DateTimeOffset? LeaseExpiresAtUtc { get; set; }

        internal bool IsOwned(string owner, long version) =>
            string.Equals(LeaseOwner, owner, StringComparison.Ordinal) && Message.Version == version;
    }
}