//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>
/// Durable <see cref="IClaimCheckStore"/> on Entity Framework Core: one row per captured
/// payload. Singleton-safe: every operation opens a short-lived context from the factory.
/// </summary>
/// <param name="factory">Creates short-lived contexts. Never <see langword="null"/>.</param>
/// <param name="clock">Stamps new payloads. Never <see langword="null"/>.</param>
public sealed class EfClaimCheckStore(IDbContextFactory<IdempotencyDbContext> factory, TimeProvider clock) : IClaimCheckStore {
    private readonly IDbContextFactory<IdempotencyDbContext> _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <inheritdoc />
    public async Task<string> PutAsync(ReadOnlyMemory<byte> payload, string contentType, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        var key = Guid.NewGuid().ToString("N");

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ClaimChecks.Add(new ClaimCheckRecord {
            Key = key,
            ContentType = contentType,
            Payload = payload.ToArray(),
            CreatedAtUtc = _clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return key;
    }

    /// <inheritdoc />
    public async Task<ClaimCheckPayload?> GetAsync(string key, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var record = await db.ClaimChecks.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Key == key, cancellationToken).ConfigureAwait(false);

        return record is null ? null : new ClaimCheckPayload(record.Payload, record.ContentType);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.ClaimChecks.Where(record => record.Key == key).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
