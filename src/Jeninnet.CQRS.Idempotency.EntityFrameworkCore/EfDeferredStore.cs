//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>
/// Durable <see cref="IDeferredStore"/> on Entity Framework Core: one row per staged command
/// (key + type + payload-or-claim-check + due + expiry), safe under concurrent writers.
/// Singleton-safe: every operation opens a short-lived context from the factory.
/// </summary>
/// <remarks>
/// Envelopes are rebuilt through the generated <see cref="IDeferredEnvelopeFactory"/> when one is
/// registered; the reflection fallback in <see cref="DeferredEnvelopeSerializer"/> only handles
/// command types outside the generated registry. Rows whose command type no longer resolves are
/// surfaced as <see cref="UnresolvedCommandEnvelope"/> so the pump dead-letters them; corrupt
/// payloads are completed (dropped) on listing because they can never execute again. Payloads
/// above the claim-check threshold live in the claim-check table; rows written before
/// claim-check keep an inline payload and stay readable.
/// </remarks>
public sealed class EfDeferredStore( // NOSONAR: Public options/records intentionally group related settings; splitting would fragment the API.
    IDbContextFactory<IdempotencyDbContext> factory,
    IOptions<EfDeferredStoreOptions> options,
    IMessageSerializer serializer,
    ClaimCheckPolicy claimChecks,
    MessageUpgradePolicy upgrades,
    IDeferredEnvelopeFactory? envelopeFactory = null,
    TimeProvider? clock = null,
    ITenantContext? tenant = null) : IKeyedDeferredStore {
    private readonly IDbContextFactory<IdempotencyDbContext> _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly EfDeferredStoreOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value; // NOSONAR: Constructor parameter is retained for API compatibility; options are currently unused.
    private readonly IMessageSerializer _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    private readonly ClaimCheckPolicy _claimChecks = claimChecks ?? throw new ArgumentNullException(nameof(claimChecks));
    private readonly MessageUpgradePolicy _upgrades = upgrades ?? throw new ArgumentNullException(nameof(upgrades));
    private readonly IDeferredEnvelopeFactory? _envelopes = envelopeFactory;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ITenantContext? _tenant = tenant;

    private string CurrentTenantId => _tenant?.Current ?? string.Empty;

    /// <inheritdoc />
    public async Task<string> StageAsync(IDeferredEnvelope envelope, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(envelope);

        var command = envelope.Command;
        var key = Guid.NewGuid().ToString("N");
        var now = _clock.GetUtcNow();
        var bytes = _serializer.Serialize(command, command.GetType());
        var claimCheckKey = await _claimChecks.StageIfLargeAsync(bytes, _serializer.ContentType, cancellationToken).ConfigureAwait(false);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.DeferredCommands.Add(CreateRecord(key, envelope, command, bytes, claimCheckKey, now));

        try {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch(DbUpdateException) {
            // Keys are random GUIDs, so a duplicate means another stager won the same
            // key: the row is staged either way. (Await is not allowed in a catch
            // filter, so the race check lives in the handler body.)
            if(await ExistsAsync(key, cancellationToken).ConfigureAwait(false)) {
                return key;
            }

            throw;
        }

        return key;
    }

    /// <inheritdoc />
    public async Task<bool> TryStageAsync(string key, IDeferredEnvelope envelope, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(envelope);

        if(await ExistsAsync(key, cancellationToken).ConfigureAwait(false)) {
            return false;
        }

        var command = envelope.Command;
        var now = _clock.GetUtcNow();
        var bytes = _serializer.Serialize(command, command.GetType());
        var claimCheckKey = await _claimChecks.StageIfLargeAsync(bytes, _serializer.ContentType, cancellationToken).ConfigureAwait(false);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.DeferredCommands.Add(CreateRecord(key, envelope, command, bytes, claimCheckKey, now));

        try {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch(DbUpdateException) {
            // Another instance staged the same deterministic occurrence key first.
            return false;
        }
    }

    private DeferredRecord CreateRecord(string key, IDeferredEnvelope envelope, object command, byte[] bytes, string? claimCheckKey, DateTimeOffset now) {
        var recurring = envelope as IRecurringEnvelope;

        return new DeferredRecord {
            TenantId = envelope.TenantId ?? CurrentTenantId,
            Key = key,
            CommandType = command.GetType().AssemblyQualifiedName ?? command.GetType().FullName ?? command.GetType().Name,
            Payload = claimCheckKey is null ? PayloadText.Encode(bytes) : string.Empty,
            ClaimCheckKey = claimCheckKey,
            MessageVersion = Math.Max(envelope.Version, _upgrades.CurrentVersion(command.GetType())),
            RecurringKey = recurring?.UniqueKey,
            CronExpression = recurring?.Expression,
            TimeZoneId = recurring?.TimeZone.Id,
            RecurringOverlap = recurring is null ? null : (int)recurring.Overlap,
            DueUtc = envelope.DueUtc,
            StoredAtUtc = now,
            ExpiresAtUtc = envelope.ExpiresAtUtc,
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(string Key, IDeferredEnvelope Envelope)>> ListAsync(CancellationToken cancellationToken = default) {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var records = await db.DeferredCommands.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var listed = new List<(string Key, IDeferredEnvelope Envelope)>(records.Count);

        foreach(var record in records) {
            var claimed = await _claimChecks.GetClaimAsync(record.ClaimCheckKey, _serializer.ContentType, cancellationToken).ConfigureAwait(false);
            var bytes = claimed ?? PayloadText.Decode(record.Payload);
            IDeferredEnvelope? envelope;

            try {
                envelope = DeferredEnvelopeSerializer.Rebuild(
                    record.CommandType,
                    bytes,
                    record.DueUtc,
                    record.ExpiresAtUtc,
                    _serializer,
                    record.MessageVersion ?? 1,
                    _upgrades,
                    record.RecurringKey is null
                        ? null
                        : new RecurringEnvelopeMetadata(
                            record.RecurringKey,
                            record.CronExpression ?? "* * * * *",
                            record.TimeZoneId ?? TimeZoneInfo.Utc.Id,
                            (RecurringOverlapPolicy)(record.RecurringOverlap ?? 0)),
                    _envelopes,
                    record.TenantId);
            }
            catch(InvalidOperationException) {
                envelope = null;
            }

            if(envelope is null) {
                // The command type is gone (or the payload is corrupt): the entry can never
                // execute, so complete it instead of replaying a ghost forever. The listing
                // context sees every tenant, so delete by the row's own composite key.
                db.DeferredCommands.Remove(record);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await _claimChecks.DeleteAsync(record.ClaimCheckKey, cancellationToken).ConfigureAwait(false);

                continue;
            }

            listed.Add((record.Key, envelope));
        }

        return listed;
    }

    /// <inheritdoc />
    public async Task CompleteAsync(string key, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var record = await db.DeferredCommands.FindAsync([CurrentTenantId, key], cancellationToken).ConfigureAwait(false);

        if(record is null) {
            return;
        }

        var claimCheckKey = record.ClaimCheckKey;
        db.DeferredCommands.Remove(record);

        try {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch(DbUpdateConcurrencyException) {
            // Another completer won the same row; the entry is gone either way.
        }

        await _claimChecks.DeleteAsync(claimCheckKey, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> PruneExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default) {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // SQLite cannot translate DateTimeOffset comparisons, so the expiry cut itself
        // happens in memory; only the null pre-filter runs in the database.
        var candidates = await db.DeferredCommands
            .AsNoTracking()
            .Where(static record => record.ExpiresAtUtc != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var expired = candidates.Where(record => record.ExpiresAtUtc <= now).ToList();

        if(expired.Count == 0) {
            return 0;
        }

        // Delete by each row's own composite key: an unscoped maintenance pass sees every
        // tenant, so filtering by the ambient tenant would leave tenant rows behind.
        db.DeferredCommands.RemoveRange(expired);

        try {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch(DbUpdateConcurrencyException) {
            return 0; // Another pruner won the same rows.
        }

        foreach(var record in expired) {
            await _claimChecks.DeleteAsync(record.ClaimCheckKey, cancellationToken).ConfigureAwait(false);
        }

        return expired.Count;
    }

    private async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken) {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.DeferredCommands.AnyAsync(record => record.Key == key, cancellationToken).ConfigureAwait(false);
    }
}
