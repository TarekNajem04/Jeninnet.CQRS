//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>
/// Durable pending-request inbox on EF Core. Rows exist only while a client is waiting; response
/// delivery remains in-process, so restarting the client discards its waiter and the TTL pruning
/// service eventually removes the row.
/// </summary>
/// <param name="factory">Creates short-lived contexts. Never <see langword="null"/>.</param>
/// <param name="tenant">The ambient tenant; <see langword="null"/> without tenancy.</param>
public sealed class EfRequestInbox(IDbContextFactory<IdempotencyDbContext> factory, ITenantContext? tenant = null) : IRequestInbox {
    private readonly IDbContextFactory<IdempotencyDbContext> _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly ITenantContext? _tenant = tenant;

    private string CurrentTenantId => _tenant?.Current ?? string.Empty;

    /// <inheritdoc />
    public async Task AddAsync(RequestInboxEntry entry, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(entry);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.RequestInbox.Add(new RequestInboxRecord {
            TenantId = entry.TenantId ?? CurrentTenantId,
            RequestId = entry.RequestId,
            RequestType = TypeName(entry.RequestType),
            ResponseType = TypeName(entry.ResponseType),
            Mode = (int)entry.Mode,
            CreatedAtUtc = entry.CreatedAtUtc,
            ExpiresAtUtc = entry.ExpiresAtUtc,
        });

        try {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch(DbUpdateException exception) {
            throw new InvalidOperationException($"A request with id '{entry.RequestId}' is already pending.", exception);
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string requestId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.RequestInbox
            .Where(record => record.TenantId == CurrentTenantId && record.RequestId == requestId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RequestInboxEntry>> ListAsync(CancellationToken cancellationToken = default) {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.RequestInbox.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows
            .Select(row => new RequestInboxEntry(
                row.RequestId,
                Resolve(row.RequestType),
                Resolve(row.ResponseType),
                (RequestResponseMode)row.Mode,
                row.CreatedAtUtc,
                row.ExpiresAtUtc,
                row.TenantId))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<int> RemoveExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default) {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var candidates = await db.RequestInbox
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var expired = candidates.Where(record => record.ExpiresAtUtc <= now).ToList();

        if(expired.Count is 0) {
            return 0;
        }

        db.RequestInbox.RemoveRange(expired);

        try {
            return await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch(DbUpdateConcurrencyException) {
            return 0; // Another pruner won the same rows.
        }
    }

    private static string TypeName(Type type) =>
        type.AssemblyQualifiedName ?? type.FullName ?? type.Name;

    private static Type Resolve(string typeName) =>
        Type.GetType(typeName, throwOnError: false) ?? typeof(object);
}
