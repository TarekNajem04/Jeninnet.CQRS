//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency;

/// <summary>
/// Default in-memory implementation of <see cref="IIdempotencyStore"/>. Completed responses and
/// execution leases live in one process-local table; acquisition is atomic because every entry
/// is mutated under its own lock.
/// </summary>
/// <remarks>
/// Completed responses never expire here (matching the historical behavior). Leases expire after
/// their TTL, so a crashed in-process owner cannot block the key forever.
/// </remarks>
/// <param name="clock">The clock used for lease expiry; defaults to <see cref="TimeProvider.System"/>.</param>
/// <param name="tenant">The ambient tenant; scopes caller-supplied keys per tenant.</param>
internal sealed class DefaultIdempotencyStore(TimeProvider? clock = null, ITenantContext? tenant = null) : IIdempotencyStore {
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ITenantContext? _tenant = tenant;

    /// <inheritdoc />
    public Task<TResponse?> GetAsync<TResponse>(string key, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);

        if(!_entries.TryGetValue(ScopedKey(key), out var entry)) {
            return Task.FromResult<TResponse?>(default);
        }

        lock(entry.Gate) {
            return string.Equals(entry.ResponseType, typeof(TResponse).AssemblyQualifiedName, StringComparison.Ordinal)
                ? Task.FromResult((TResponse?)entry.Response)
                : Task.FromResult<TResponse?>(default);
        }
    }

    /// <inheritdoc />
    public Task StoreAsync<TResponse>(string key, TResponse response, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);

        var entry = _entries.GetOrAdd(ScopedKey(key), static _ => new Entry());

        lock(entry.Gate) {
            entry.ResponseType = typeof(TResponse).AssemblyQualifiedName;
            entry.Response = response;
            entry.Owner = null;
            entry.LeaseExpiresAtUtc = null;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IdempotencyLeaseResult<TResponse>> TryAcquireAsync<TResponse>(string key, string owner, TimeSpan timeToLive, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);
        Guard.NotNullOrWhiteSpace(owner);
        ValidateLease(timeToLive, nameof(timeToLive));

        var entry = _entries.GetOrAdd(ScopedKey(key), static _ => new Entry());

        lock(entry.Gate) {
            var now = _clock.GetUtcNow();

            // A completed response under the expected type wins over a stale lease/owner marker.
            if(string.Equals(entry.ResponseType, typeof(TResponse).AssemblyQualifiedName, StringComparison.Ordinal)) {
                return Task.FromResult(IdempotencyLeaseResult<TResponse>.Completed((TResponse?)entry.Response));
            }

            if(entry.Owner is not null && entry.LeaseExpiresAtUtc is { } expiresAtUtc && expiresAtUtc > now) {
                return Task.FromResult(IdempotencyLeaseResult<TResponse>.Busy(entry.Owner, expiresAtUtc));
            }

            var leaseExpiresAtUtc = now + timeToLive;
            entry.Owner = owner;
            entry.LeaseExpiresAtUtc = leaseExpiresAtUtc;

            // A response of another type is treated like a miss (the historical Get/Store
            // semantics): the new execution owns the key and will overwrite it on completion.
            entry.ResponseType = null;
            entry.Response = null;

            return Task.FromResult(IdempotencyLeaseResult<TResponse>.Acquired(owner, leaseExpiresAtUtc));
        }
    }

    /// <inheritdoc />
    public Task<bool> RenewAsync(string key, string owner, TimeSpan timeToLive, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);
        Guard.NotNullOrWhiteSpace(owner);
        ValidateLease(timeToLive, nameof(timeToLive));

        if(!_entries.TryGetValue(ScopedKey(key), out var entry)) {
            return Task.FromResult(false);
        }

        lock(entry.Gate) {
            var now = _clock.GetUtcNow();

            if(!string.Equals(entry.Owner, owner, StringComparison.Ordinal) ||
               entry.LeaseExpiresAtUtc is not { } expiresAtUtc ||
               expiresAtUtc <= now) {
                return Task.FromResult(false);
            }

            entry.LeaseExpiresAtUtc = now + timeToLive;

            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> CompleteAsync<TResponse>(string key, string owner, TResponse response, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);
        Guard.NotNullOrWhiteSpace(owner);

        if(!_entries.TryGetValue(ScopedKey(key), out var entry)) {
            return Task.FromResult(false);
        }

        lock(entry.Gate) {
            if(!string.Equals(entry.Owner, owner, StringComparison.Ordinal)) {
                return Task.FromResult(false);
            }

            entry.ResponseType = typeof(TResponse).AssemblyQualifiedName;
            entry.Response = response;
            entry.Owner = null;
            entry.LeaseExpiresAtUtc = null;

            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> ReleaseAsync(string key, string owner, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);
        Guard.NotNullOrWhiteSpace(owner);

        if(!_entries.TryGetValue(ScopedKey(key), out var entry)) {
            return Task.FromResult(false);
        }

        lock(entry.Gate) {
            if(!string.Equals(entry.Owner, owner, StringComparison.Ordinal)) {
                return Task.FromResult(false);
            }

            entry.Owner = null;
            entry.LeaseExpiresAtUtc = null;

            return Task.FromResult(true);
        }
    }

    private static void ValidateLease(TimeSpan timeToLive, string parameterName) {
        if(timeToLive <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(parameterName, timeToLive, "The lease time-to-live must be greater than zero.");
        }
    }

    private string ScopedKey(string key) =>
        _tenant?.Current is { Length: > 0 } currentTenant ? $"{currentTenant}:{key}" : key;

    private sealed class Entry {
        internal object Gate { get; } = new();

        internal string? Owner { get; set; }

        internal DateTimeOffset? LeaseExpiresAtUtc { get; set; }

        internal string? ResponseType { get; set; }

        internal object? Response { get; set; }
    }
}