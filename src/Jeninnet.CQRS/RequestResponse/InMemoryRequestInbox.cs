//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.RequestResponse;

/// <summary>In-memory pending-request table: the default used when no durable inbox is registered.</summary>
/// <param name="tenant">The ambient tenant; scopes listings and expiry cleanup.</param>
public sealed class InMemoryRequestInbox(ITenantContext? tenant = null) : IRequestInbox {
    private readonly Lock _sync = new();
    private readonly Dictionary<string, RequestInboxEntry> _entries = new(StringComparer.Ordinal);
    private readonly ITenantContext? _tenant = tenant;

    /// <inheritdoc />
    public Task AddAsync(RequestInboxEntry entry, CancellationToken cancellationToken = default) {
        Guard.NotNull(entry);

        if(entry.TenantId is null && _tenant?.Current is { Length: > 0 } current) {
            entry = entry with { TenantId = current };
        }

        lock(_sync) {
            if(_entries.ContainsKey(entry.RequestId)) {
                throw new InvalidOperationException($"A request with id '{entry.RequestId}' is already pending.");
            }

            _entries.Add(entry.RequestId, entry);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string requestId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(requestId);

        lock(_sync) {
            _entries.Remove(requestId);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RequestInboxEntry>> ListAsync(CancellationToken cancellationToken = default) {
        lock(_sync) {
            var current = _tenant?.Current;
            var listed = _entries.Values
                .Where(entry => StringCompat.IsNullOrEmpty(current) || entry.TenantId == current)
                .ToList();

            return Task.FromResult<IReadOnlyList<RequestInboxEntry>>(listed);
        }
    }

    /// <inheritdoc />
    public Task<int> RemoveExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default) {
        lock(_sync) {
            var current = _tenant?.Current;
            var expired = _entries.Values
                .Where(entry => (StringCompat.IsNullOrEmpty(current) || entry.TenantId == current) &&
                               entry.ExpiresAtUtc <= now)
                .Select(static entry => entry.RequestId)
                .ToList();

            foreach(var requestId in expired) {
                _entries.Remove(requestId);
            }

            return Task.FromResult(expired.Count);
        }
    }
}
