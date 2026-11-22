//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>
/// Durable <see cref="IIdempotencyStore"/> on Entity Framework Core: one row per
/// idempotency key (key + response payload or claim-check + expiry + execution lease), safe under
/// concurrent writers. Singleton-safe: every operation opens a short-lived context from the factory.
/// </summary>
/// <remarks>
/// <para>
/// Concurrency: an insert that loses to another writer becomes an update; an update
/// that loses becomes a retry (bounded by <c>MaxAttempts</c>). Concurrent duplicate
/// stores converge on one winner without surfacing an exception.
/// </para>
/// <para>
/// Execution leases use the same optimistic-concurrency token. Acquisition either inserts the row
/// (one winner) or updates an unowned/expired row; a loser retries and observes
/// <see cref="IdempotencyLeaseState.Busy"/>. Completion and renewal are owner-checked, so a stale
/// owner cannot overwrite a newer execution.
/// </para>
/// <para>
/// Payloads above the claim-check threshold live in the claim-check table; inline rows written
/// before claim-check stay readable. Overwriting a claimed response deletes the previous claim.
/// </para>
/// <para>SQLite guidance: enable WAL plus a busy timeout (see the package README)
/// so concurrent writers queue instead of failing with "database is locked".</para>
/// </remarks>
public sealed class EfIdempotencyStore(
    IDbContextFactory<IdempotencyDbContext> factory,
    IOptions<EfIdempotencyStoreOptions> options,
    IMessageSerializer serializer,
    ClaimCheckPolicy claimChecks,
    TimeProvider? clock = null,
    ITenantContext? tenant = null) : IIdempotencyStore {
    private readonly IDbContextFactory<IdempotencyDbContext> _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly EfIdempotencyStoreOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    private readonly IMessageSerializer _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    private readonly ClaimCheckPolicy _claimChecks = claimChecks ?? throw new ArgumentNullException(nameof(claimChecks));
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ITenantContext? _tenant = tenant;

    private string CurrentTenantId => _tenant?.Current ?? string.Empty;

    /// <inheritdoc />
    public async Task<TResponse?> GetAsync<TResponse>(string key, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var record = await db.Records.FindAsync([CurrentTenantId, key], cancellationToken).ConfigureAwait(false);

        if(record is null) {
            return default;
        }

        if(record.ExpiresAtUtc is not null && record.ExpiresAtUtc <= _clock.GetUtcNow()) {
            var claimCheckKey = record.ClaimCheckKey;
            db.Records.Remove(record);

            try {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch(DbUpdateConcurrencyException) {
                // Another reader pruned the same row; the miss below still holds.
            }

            await _claimChecks.DeleteAsync(claimCheckKey, cancellationToken).ConfigureAwait(false);

            return default;
        }

        // A row without a response type is leased (or free) rather than completed; a response
        // stored under another type is not ours — both are misses for the caller.
        if(string.IsNullOrEmpty(record.ResponseType) ||
           !string.Equals(record.ResponseType, typeof(TResponse).AssemblyQualifiedName, StringComparison.Ordinal)) {
            return default;
        }

        return await ReadResponseAsync<TResponse>(record, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StoreAsync<TResponse>(string key, TResponse response, CancellationToken cancellationToken = default) { // NOSONAR: Complexity is inherent to this state machine/pipeline and is covered by tests.
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var now = _clock.GetUtcNow();
        var payloadBytes = _serializer.Serialize(response!, typeof(TResponse));
        var claimCheckKey = await _claimChecks.StageIfLargeAsync(payloadBytes, _serializer.ContentType, cancellationToken).ConfigureAwait(false);
        var payload = claimCheckKey is null ? PayloadText.Encode(payloadBytes) : string.Empty;
        var typeName = typeof(TResponse).AssemblyQualifiedName ?? string.Empty;
        var expiresAt = _options.TimeToLive is null ? null : now + _options.TimeToLive;

        for(var attempt = 0; ; attempt++) { // NOSONAR: The loop exits via return/throw; the attempt counter drives backoff, not the stop condition.
            await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var existing = await db.Records.FindAsync([CurrentTenantId, key], cancellationToken).ConfigureAwait(false);

            if(existing is null) {
                db.Records.Add(new IdempotencyRecord {
                    TenantId = CurrentTenantId,
                    Key = key,
                    ResponseType = typeName,
                    Payload = payload,
                    ClaimCheckKey = claimCheckKey,
                    StoredAtUtc = now,
                    ExpiresAtUtc = expiresAt,
                });

                try {
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch(DbUpdateException) {
                    // Await is not allowed in a catch filter, so the race check lives here:
                    // when another writer won the insert, fall through to the update path.
                    if(attempt < _options.MaxAttempts && await ExistsAsync(key, cancellationToken).ConfigureAwait(false)) {
                        continue;
                    }

                    throw;
                }
            } else {
                var previousClaimCheckKey = existing.ClaimCheckKey;
                existing.ResponseType = typeName;
                existing.Payload = payload;
                existing.ClaimCheckKey = claimCheckKey;
                existing.LeaseOwner = null;
                existing.LeaseExpiresAtUtc = null;
                existing.StoredAtUtc = now;
                existing.ExpiresAtUtc = expiresAt;
                existing.Version++;

                try {
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch(DbUpdateConcurrencyException) when(attempt < _options.MaxAttempts) {
                    continue; // Lost the update race; re-read and overwrite again.
                }

                if(previousClaimCheckKey is not null && !string.Equals(previousClaimCheckKey, claimCheckKey, StringComparison.Ordinal)) {
                    await _claimChecks.DeleteAsync(previousClaimCheckKey, cancellationToken).ConfigureAwait(false);
                }

                return;
            }
        }
    }

    /// <inheritdoc />
    public async Task<IdempotencyLeaseResult<TResponse>> TryAcquireAsync<TResponse>(string key, string owner, TimeSpan timeToLive, CancellationToken cancellationToken = default) { // NOSONAR: Complexity is inherent to this state machine/pipeline and is covered by tests.
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ValidateLease(timeToLive, nameof(timeToLive));

        var typeName = typeof(TResponse).AssemblyQualifiedName ?? string.Empty;

        for(var attempt = 0; attempt <= _options.MaxAttempts; attempt++) {
            var now = _clock.GetUtcNow();
            var leaseExpiresAtUtc = now + timeToLive;
            await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var record = await db.Records.FindAsync([CurrentTenantId, key], cancellationToken).ConfigureAwait(false);

            if(record is null) {
                db.Records.Add(new IdempotencyRecord {
                    TenantId = CurrentTenantId,
                    Key = key,
                    ResponseType = string.Empty,
                    Payload = string.Empty,
                    LeaseOwner = owner,
                    LeaseExpiresAtUtc = leaseExpiresAtUtc,
                    StoredAtUtc = now,
                });

                try {
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    return IdempotencyLeaseResult<TResponse>.Acquired(owner, leaseExpiresAtUtc);
                }
                catch(DbUpdateException) {
                    // Another caller inserted the same key first; re-read and observe its lease.
                    continue;
                }
            }

            if(!string.IsNullOrEmpty(record.ResponseType)) {
                if(record.ExpiresAtUtc is not null && record.ExpiresAtUtc <= now) {
                    var expiredClaim = record.ClaimCheckKey;
                    db.Records.Remove(record);

                    try {
                        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch(DbUpdateConcurrencyException) {
                        continue; // Another caller pruned/reclaimed it; re-read.
                    }

                    await _claimChecks.DeleteAsync(expiredClaim, cancellationToken).ConfigureAwait(false);

                    continue;
                }

                if(string.Equals(record.ResponseType, typeName, StringComparison.Ordinal)) {
                    var response = await ReadResponseAsync<TResponse>(record, cancellationToken).ConfigureAwait(false);

                    return IdempotencyLeaseResult<TResponse>.Completed(response);
                }
            } else if(record.LeaseOwner is not null &&
                      record.LeaseExpiresAtUtc is { } currentExpiry &&
                      currentExpiry > now) {
                return IdempotencyLeaseResult<TResponse>.Busy(record.LeaseOwner, currentExpiry);
            }

            // Free, expired, or completed under another response type: reclaim atomically.
            var previousClaimCheckKey = record.ClaimCheckKey;
            record.ResponseType = string.Empty;
            record.Payload = string.Empty;
            record.ClaimCheckKey = null;
            record.LeaseOwner = owner;
            record.LeaseExpiresAtUtc = leaseExpiresAtUtc;
            record.StoredAtUtc = now;
            record.ExpiresAtUtc = null;
            record.Version++;

            try {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch(DbUpdateConcurrencyException) {
                continue; // Lost the acquire race; re-read and observe the winner.
            }

            if(previousClaimCheckKey is not null) {
                await _claimChecks.DeleteAsync(previousClaimCheckKey, cancellationToken).ConfigureAwait(false);
            }

            return IdempotencyLeaseResult<TResponse>.Acquired(owner, leaseExpiresAtUtc);
        }

        throw new InvalidOperationException($"Could not acquire the idempotency lease for '{key}' after {_options.MaxAttempts} retries.");
    }

    /// <inheritdoc />
    public async Task<bool> RenewAsync(string key, string owner, TimeSpan timeToLive, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ValidateLease(timeToLive, nameof(timeToLive));

        for(var attempt = 0; attempt <= _options.MaxAttempts; attempt++) {
            await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var record = await db.Records.FindAsync([CurrentTenantId, key], cancellationToken).ConfigureAwait(false);
            var now = _clock.GetUtcNow();

            if(record is null ||
               !string.Equals(record.LeaseOwner, owner, StringComparison.Ordinal) ||
               record.LeaseExpiresAtUtc is not { } currentExpiry ||
               currentExpiry <= now) {
                return false;
            }

            record.LeaseExpiresAtUtc = now + timeToLive;
            record.Version++;

            try {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return true;
            }
            catch(DbUpdateConcurrencyException) {
                continue; // Re-read: the lease may have been completed or reclaimed.
            }
        }

        return false;
    }

    /// <inheritdoc />
    public async Task<bool> CompleteAsync<TResponse>(string key, string owner, TResponse response, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        var now = _clock.GetUtcNow();
        var payloadBytes = _serializer.Serialize(response!, typeof(TResponse));
        var claimCheckKey = await _claimChecks.StageIfLargeAsync(payloadBytes, _serializer.ContentType, cancellationToken).ConfigureAwait(false);
        var payload = claimCheckKey is null ? PayloadText.Encode(payloadBytes) : string.Empty;
        var typeName = typeof(TResponse).AssemblyQualifiedName ?? string.Empty;
        var expiresAt = _options.TimeToLive is null ? null : now + _options.TimeToLive;

        for(var attempt = 0; attempt <= _options.MaxAttempts; attempt++) {
            await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var record = await db.Records.FindAsync([CurrentTenantId, key], cancellationToken).ConfigureAwait(false);

            if(record is null || !string.Equals(record.LeaseOwner, owner, StringComparison.Ordinal)) {
                await DeleteStagedClaimAsync(claimCheckKey, cancellationToken).ConfigureAwait(false);

                return false;
            }

            var previousClaimCheckKey = record.ClaimCheckKey;
            record.ResponseType = typeName;
            record.Payload = payload;
            record.ClaimCheckKey = claimCheckKey;
            record.LeaseOwner = null;
            record.LeaseExpiresAtUtc = null;
            record.StoredAtUtc = now;
            record.ExpiresAtUtc = expiresAt;
            record.Version++;

            try {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch(DbUpdateConcurrencyException) {
                continue; // Re-read: only the owner may complete, and ownership is re-checked.
            }

            if(previousClaimCheckKey is not null && !string.Equals(previousClaimCheckKey, claimCheckKey, StringComparison.Ordinal)) {
                await _claimChecks.DeleteAsync(previousClaimCheckKey, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }

        await DeleteStagedClaimAsync(claimCheckKey, cancellationToken).ConfigureAwait(false);

        return false;
    }

    /// <inheritdoc />
    public async Task<bool> ReleaseAsync(string key, string owner, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        for(var attempt = 0; attempt <= _options.MaxAttempts; attempt++) {
            await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var record = await db.Records.FindAsync([CurrentTenantId, key], cancellationToken).ConfigureAwait(false);

            if(record is null || !string.Equals(record.LeaseOwner, owner, StringComparison.Ordinal)) {
                return false;
            }

            record.LeaseOwner = null;
            record.LeaseExpiresAtUtc = null;
            record.Version++;

            try {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return true;
            }
            catch(DbUpdateConcurrencyException) {
                continue; // Re-read: another caller completed or reclaimed the row.
            }
        }

        return false;
    }

    /// <summary>
    /// Deletes expired entries. Returns the number of rows removed.
    /// Call periodically (for example from an <c>IHostedService</c> timer);
    /// see the package README for a snippet.
    /// </summary>
    public async Task<int> PruneExpiredAsync(CancellationToken cancellationToken = default) {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // SQLite cannot translate DateTimeOffset comparisons, so the expiry cut itself
        // happens in memory; only the null pre-filter runs in the database.
        var candidates = await db.Records
            .Where(static record => record.ExpiresAtUtc != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var now = _clock.GetUtcNow();
        var due = candidates.Where(record => record.ExpiresAtUtc <= now).ToList();

        if(due.Count == 0) {
            return 0;
        }

        db.Records.RemoveRange(due);

        try {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch(DbUpdateConcurrencyException) {
            return 0; // Another pruner won the same rows; nothing left for us.
        }

        foreach(var record in due) {
            await _claimChecks.DeleteAsync(record.ClaimCheckKey, cancellationToken).ConfigureAwait(false);
        }

        return due.Count;
    }

    private async Task<TResponse?> ReadResponseAsync<TResponse>(IdempotencyRecord record, CancellationToken cancellationToken) {
        var claimed = await _claimChecks.GetClaimAsync(record.ClaimCheckKey, _serializer.ContentType, cancellationToken).ConfigureAwait(false);
        var bytes = claimed ?? PayloadText.Decode(record.Payload);
        var value = _serializer.Deserialize(bytes, typeof(TResponse));

        return value is null ? default : (TResponse)value;
    }

    private async Task DeleteStagedClaimAsync(string? claimCheckKey, CancellationToken cancellationToken) {
        if(claimCheckKey is not null) {
            await _claimChecks.DeleteAsync(claimCheckKey, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken) {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Records.AnyAsync(record => record.Key == key, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateLease(TimeSpan timeToLive, string parameterName) {
        if(timeToLive <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(parameterName, timeToLive, "The lease time-to-live must be greater than zero.");
        }
    }
}