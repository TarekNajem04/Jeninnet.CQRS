//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>
/// Durable <see cref="IDeadLetterStore"/> on Entity Framework Core: one row per failed message,
/// with attempt count, last error, and the payload (inline or claim-checked) needed to replay it
/// after a restart. Singleton-safe: every operation opens a short-lived context from the factory.
/// </summary>
/// <remarks>
/// Envelope rebuilding uses the generated registry first and runtime type resolution as the
/// documented fallback, which is why this package is outside the NativeAOT story (see the
/// package README). Rows whose command type no longer resolves surface as
/// <see cref="UnresolvedCommandEnvelope"/> and stay inspectable; corrupt payloads are removed on
/// listing or replay because they can never execute again. Payloads above the claim-check
/// threshold live in the claim-check table; inline rows written before claim-check stay readable.
/// </remarks>
public sealed class EfDeadLetterStore(
    IDbContextFactory<IdempotencyDbContext> factory,
    IMessageSerializer serializer,
    ClaimCheckPolicy claimChecks,
    IOptions<DeadLetterOptions> options,
    IDeferredEnvelopeFactory? envelopeFactory = null,
    ITenantContext? tenant = null) : IDeadLetterStore {
    private readonly IDbContextFactory<IdempotencyDbContext> _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly IMessageSerializer _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    private readonly ClaimCheckPolicy _claimChecks = claimChecks ?? throw new ArgumentNullException(nameof(claimChecks));
    private readonly DeadLetterOptions _options = Validate((options ?? throw new ArgumentNullException(nameof(options))).Value);
    private readonly IDeferredEnvelopeFactory? _envelopes = envelopeFactory;
    private readonly ITenantContext? _tenant = tenant;

    private string CurrentTenantId => _tenant?.Current ?? string.Empty;

    /// <inheritdoc />
    public async Task<string> RecordAsync(IDeferredEnvelope envelope, Exception error, DateTimeOffset failedAtUtc, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(error);

        var command = envelope.Command;
        var key = Guid.NewGuid().ToString("N");

        // An unresolved row has no command instance to serialize; keep its stored type name so
        // the dead letter stays inspectable and a replay keeps failing loudly instead of
        // pretending the message was something else.
        var unresolved = envelope as UnresolvedCommandEnvelope;
        var commandTypeName = unresolved?.CommandTypeName ?? command.GetType().AssemblyQualifiedName ?? command.GetType().FullName ?? command.GetType().Name;
        var bytes = unresolved is null ? _serializer.Serialize(command, command.GetType()) : [];
        var claimCheckKey = await _claimChecks.StageIfLargeAsync(bytes, _serializer.ContentType, cancellationToken).ConfigureAwait(false);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.DeadLetters.Add(new DeadLetterRecord {
            TenantId = envelope.TenantId ?? CurrentTenantId,
            Key = key,
            CommandType = commandTypeName,
            Payload = claimCheckKey is null ? PayloadText.Encode(bytes) : string.Empty,
            ClaimCheckKey = claimCheckKey,
            DueUtc = envelope.DueUtc,
            ExpiresAtUtc = envelope.ExpiresAtUtc,
            Attempts = 1,
            LastErrorType = DeadLetterEntry.ErrorTypeOf(error),
            LastErrorMessage = error.Message,
            FirstFailedAtUtc = failedAtUtc,
            LastFailedAtUtc = failedAtUtc,
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await TrimAsync(db, cancellationToken).ConfigureAwait(false);

        return key;
    }

    /// <inheritdoc />
    public async Task RecordFailureAsync(string key, Exception error, DateTimeOffset failedAtUtc, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(error);

        var errorType = DeadLetterEntry.ErrorTypeOf(error);
        var errorMessage = error.Message;

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // One atomic statement: concurrent recorders cannot lose an increment.
        await db.DeadLetters
            .Where(record => record.TenantId == CurrentTenantId && record.Key == key)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(record => record.Attempts, record => record.Attempts + 1)
                    .SetProperty(record => record.LastErrorType, errorType)
                    .SetProperty(record => record.LastErrorMessage, errorMessage)
                    .SetProperty(record => record.LastFailedAtUtc, failedAtUtc),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeadLetterEntry>> ListAsync(CancellationToken cancellationToken = default) {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var records = await db.DeadLetters.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var listed = new List<DeadLetterEntry>(records.Count);
        var ghosts = new List<DeadLetterRecord>();

        foreach(var record in records) {
            var envelope = await RebuildAsync(record, cancellationToken).ConfigureAwait(false);

            if(envelope is null) {
                ghosts.Add(record);
                continue;
            }

            listed.Add(ToEntry(record, envelope));
        }

        if(ghosts.Count > 0) {
            db.DeadLetters.RemoveRange(ghosts);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            foreach(var record in ghosts) {
                await _claimChecks.DeleteAsync(record.ClaimCheckKey, cancellationToken).ConfigureAwait(false);
            }
        }

        listed.Sort(static (left, right) => {
            var byTime = right.LastFailedAtUtc.CompareTo(left.LastFailedAtUtc);

            return byTime != 0 ? byTime : string.CompareOrdinal(left.Key, right.Key);
        });

        return listed;
    }

    /// <inheritdoc />
    public async Task<DeadLetterEntry?> GetAsync(string key, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var record = await db.DeadLetters.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Key == key, cancellationToken).ConfigureAwait(false);

        if(record is null) {
            return null;
        }

        var envelope = await RebuildAsync(record, cancellationToken).ConfigureAwait(false);

        if(envelope is null) {
            // The command type is gone (or the payload is corrupt): drop the ghost, it can
            // never be replayed.
            await db.DeadLetters
                .Where(candidate => candidate.TenantId == CurrentTenantId && candidate.Key == key)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            await _claimChecks.DeleteAsync(record.ClaimCheckKey, cancellationToken).ConfigureAwait(false);

            return null;
        }

        return ToEntry(record, envelope);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var record = await db.DeadLetters.FindAsync([key], cancellationToken).ConfigureAwait(false);

        if(record is null || !string.Equals(record.TenantId, CurrentTenantId, StringComparison.Ordinal)) {
            return;
        }

        var claimCheckKey = record.ClaimCheckKey;
        db.DeadLetters.Remove(record);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _claimChecks.DeleteAsync(claimCheckKey, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> PruneAsync(DateTimeOffset now, CancellationToken cancellationToken = default) {
        var cutoff = now - _options.Retention;
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // SQLite cannot translate DateTimeOffset comparisons, so the cutoff runs in memory.
        var candidates = await db.DeadLetters
            .AsNoTracking()
            .Select(static record => new { record.TenantId, record.Key, record.LastFailedAtUtc, record.ClaimCheckKey })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var expired = candidates.Where(candidate => candidate.LastFailedAtUtc < cutoff).ToList();
        var removed = 0;

        if(expired.Count > 0) {
            removed = await DeleteByTenantAsync(db, expired.Select(static candidate => (candidate.TenantId, candidate.Key)).ToList(), cancellationToken).ConfigureAwait(false);

            foreach(var candidate in expired) {
                await _claimChecks.DeleteAsync(candidate.ClaimCheckKey, cancellationToken).ConfigureAwait(false);
            }
        }

        removed += await TrimAsync(db, cancellationToken).ConfigureAwait(false);

        return removed;
    }

    private async Task<int> TrimAsync(IdempotencyDbContext db, CancellationToken cancellationToken) {
        var candidates = await db.DeadLetters
            .AsNoTracking()
            .Select(static record => new { record.TenantId, record.Key, record.LastFailedAtUtc, record.ClaimCheckKey })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var excess = candidates.Count - _options.MaxEntries;

        if(excess <= 0) {
            return 0;
        }

        var evicted = candidates
            .OrderBy(static candidate => candidate.LastFailedAtUtc)
            .ThenBy(static candidate => candidate.Key, StringComparer.Ordinal)
            .Take(excess)
            .ToList();
        var removed = await DeleteByTenantAsync(db, evicted.Select(static candidate => (candidate.TenantId, candidate.Key)).ToList(), cancellationToken).ConfigureAwait(false);

        foreach(var candidate in evicted) {
            await _claimChecks.DeleteAsync(candidate.ClaimCheckKey, cancellationToken).ConfigureAwait(false);
        }

        return removed;
    }

    private static async Task<int> DeleteByTenantAsync(
        IdempotencyDbContext db,
        IReadOnlyList<(string TenantId, string Key)> rows,
        CancellationToken cancellationToken) {
        var removed = 0;

        foreach(var group in rows.GroupBy(static row => row.TenantId, StringComparer.Ordinal)) {
            var tenant = group.Key;
            var keys = group.Select(static row => row.Key).ToList();
            removed += await db.DeadLetters
                .Where(record => record.TenantId == tenant && keys.Contains(record.Key))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return removed;
    }

    private async Task<IDeferredEnvelope?> RebuildAsync(DeadLetterRecord record, CancellationToken cancellationToken) {
        var claimed = await _claimChecks.GetClaimAsync(record.ClaimCheckKey, _serializer.ContentType, cancellationToken).ConfigureAwait(false);
        var bytes = claimed ?? PayloadText.Decode(record.Payload);

        return DeferredEnvelopeSerializer.Rebuild(
            record.CommandType,
            bytes,
            record.DueUtc,
            record.ExpiresAtUtc,
            _serializer,
            factory: _envelopes,
            tenantId: record.TenantId);
    }

    private static DeadLetterEntry ToEntry(DeadLetterRecord record, IDeferredEnvelope envelope) =>
        new(record.Key, envelope, record.Attempts, record.LastErrorType, record.LastErrorMessage, record.FirstFailedAtUtc, record.LastFailedAtUtc);

    private static DeadLetterOptions Validate(DeadLetterOptions options) {
        if(options.MaxEntries < 1) {
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxEntries, "MaxEntries must be at least 1.");
        }

        if(options.Retention < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options), options.Retention, "Retention must not be negative.");
        }

        return options;
    }
}