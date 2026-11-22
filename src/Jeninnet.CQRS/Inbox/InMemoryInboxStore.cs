//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Inbox;

/// <summary>
/// In-process <see cref="IInboxStore"/>. Every mutation takes one lock, so the first concurrent
/// delivery of a message id wins and the others observe a duplicate. Rows are process-local; the
/// EF Core store is the restart-safe replacement.
/// </summary>
/// <param name="clock">The clock used for pending-row expiry; defaults to <see cref="TimeProvider.System"/>.</param>
public sealed class InMemoryInboxStore(TimeProvider? clock = null) : IInboxStore {
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Lock _sync = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <inheritdoc />
    public Task<bool> TryBeginAsync(InboxMessage message, CancellationToken cancellationToken = default) {
        Guard.NotNull(message);
        Guard.NotNullOrWhiteSpace(message.MessageId);

        var key = Key(message.MessageId, message.TenantId);
        var now = _clock.GetUtcNow();

        lock(_sync) {
            if(_entries.TryGetValue(key, out var existing)) {
                if(existing.ProcessedAtUtc is not null) {
                    return Task.FromResult(false);
                }

                if(existing.ExpiresAtUtc is { } expiresAtUtc && expiresAtUtc > now) {
                    return Task.FromResult(false);
                }

                existing.ReceivedAtUtc = message.ReceivedAtUtc;
                existing.ExpiresAtUtc = message.ExpiresAtUtc;
                existing.ResultHash = null;

                return Task.FromResult(true);
            }

            _entries[key] = new Entry {
                MessageId = message.MessageId,
                TenantId = message.TenantId ?? string.Empty,
                ReceivedAtUtc = message.ReceivedAtUtc,
                ExpiresAtUtc = message.ExpiresAtUtc,
            };

            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task CompleteAsync(
        string messageId,
        string? tenantId,
        string? resultHash,
        DateTimeOffset processedAtUtc,
        CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(messageId);

        lock(_sync) {
            if(_entries.TryGetValue(Key(messageId, tenantId), out var entry) &&
               entry.ProcessedAtUtc is null) {
                entry.ProcessedAtUtc = processedAtUtc;
                entry.ResultHash = resultHash;
                entry.ExpiresAtUtc = null;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AbandonAsync(string messageId, string? tenantId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(messageId);

        lock(_sync) {
            var key = Key(messageId, tenantId);

            if(_entries.TryGetValue(key, out var entry) && entry.ProcessedAtUtc is null) {
                _entries.Remove(key);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<InboxEntry?> GetAsync(string messageId, string? tenantId = null, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(messageId);

        lock(_sync) {
            return Task.FromResult(
                _entries.TryGetValue(Key(messageId, tenantId), out var entry) ? entry.ToEntry() : null);
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsProcessedAsync(string messageId, string? tenantId = null, CancellationToken cancellationToken = default) {
        var entry = await GetAsync(messageId, tenantId, cancellationToken).ConfigureAwait(false);

        return entry?.IsProcessed is true;
    }

    /// <inheritdoc />
    public Task<int> PruneAsync(
        DateTimeOffset processedBeforeUtc,
        DateTimeOffset pendingBeforeUtc,
        CancellationToken cancellationToken = default) {
        lock(_sync) {
            var due = _entries.Values
                .Where(entry =>
                    entry.ProcessedAtUtc is { } processedAtUtc
                        ? processedAtUtc <= processedBeforeUtc
                        : entry.ExpiresAtUtc is { } expiresAtUtc && expiresAtUtc <= pendingBeforeUtc)
                .Select(static entry => Key(entry.MessageId, entry.TenantId))
                .ToList();

            foreach(var key in due) {
                _entries.Remove(key);
            }

            return Task.FromResult(due.Count);
        }
    }

    private static string Key(string messageId, string? tenantId) =>
        string.Concat(tenantId ?? string.Empty, ":", messageId);

    private sealed class Entry {
        internal string MessageId { get; init; } = string.Empty;

        internal string TenantId { get; init; } = string.Empty;

        internal DateTimeOffset ReceivedAtUtc { get; set; }

        internal DateTimeOffset? ProcessedAtUtc { get; set; }

        internal string? ResultHash { get; set; }

        internal DateTimeOffset? ExpiresAtUtc { get; set; }

        internal InboxEntry ToEntry() =>
            new(MessageId, TenantId, ReceivedAtUtc, ProcessedAtUtc, ResultHash, ExpiresAtUtc);
    }
}