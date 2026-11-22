//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.DeferredDelivery;

/// <summary>
/// In-memory <see cref="IDeferredStore"/>: the historical behavior. Entries live
/// only as long as the process; restart recovery finds nothing. Singleton-safe.
/// </summary>
/// <param name="tenant">The ambient tenant; scopes deterministic recurring keys per tenant.</param>
public sealed class InMemoryDeferredStore(ITenantContext? tenant = null) : IKeyedDeferredStore {
    private readonly Lock _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ITenantContext? _tenant = tenant;

    /// <inheritdoc />
    public Task<string> StageAsync(IDeferredEnvelope envelope, CancellationToken cancellationToken = default) {
        Guard.NotNull(envelope);

        var key = Guid.NewGuid().ToString("N");
        lock(_sync) {
            _entries[ScopedKey(key)] = new Entry(key, envelope);
        }

        return Task.FromResult(key);
    }

    /// <inheritdoc />
    public Task<bool> TryStageAsync(string key, IDeferredEnvelope envelope, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);
        Guard.NotNull(envelope);

        lock(_sync) {
            var scoped = ScopedKey(key);

            if(_entries.ContainsKey(scoped)) {
                return Task.FromResult(false);
            }

            _entries.Add(scoped, new Entry(key, envelope));

            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<(string Key, IDeferredEnvelope Envelope)>> ListAsync(CancellationToken cancellationToken = default) {
        lock(_sync) {
            var current = _tenant?.Current;
            var listed = _entries
                .Where(pair => StringCompat.IsNullOrEmpty(current) || pair.Value.Envelope.TenantId == current)
                .Select(static pair => (pair.Value.Key, pair.Value.Envelope))
                .ToList();

            return Task.FromResult<IReadOnlyList<(string Key, IDeferredEnvelope Envelope)>>(listed);
        }
    }

    /// <inheritdoc />
    public Task CompleteAsync(string key, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);

        lock(_sync) {
            _entries.Remove(ScopedKey(key));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> PruneExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default) {
        List<string> expired;
        lock(_sync) {
            var current = _tenant?.Current;
            expired = _entries
                .Where(pair => (StringCompat.IsNullOrEmpty(current) || pair.Value.Envelope.TenantId == current) &&
                               pair.Value.Envelope.ExpiresAtUtc is not null &&
                               pair.Value.Envelope.ExpiresAtUtc <= now)
                .Select(static pair => pair.Key)
                .ToList();

            foreach(var key in expired) {
                _entries.Remove(key);
            }
        }

        return Task.FromResult(expired.Count);
    }

    private string ScopedKey(string key) =>
        _tenant?.Current is { Length: > 0 } currentTenant ? $"{currentTenant}:{key}" : key;

    private sealed record Entry(string Key, IDeferredEnvelope Envelope);
}