//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.ClaimCheck;

/// <summary>
/// In-memory <see cref="IClaimCheckStore"/>: the default. Payloads live only as long as the
/// process; use the EF Core store for restart safety. Singleton-safe.
/// </summary>
public sealed class InMemoryClaimCheckStore : IClaimCheckStore {
    private readonly Lock _sync = new();
    private readonly Dictionary<string, ClaimCheckPayload> _entries = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<string> PutAsync(ReadOnlyMemory<byte> payload, string contentType, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(contentType);

        var key = Guid.NewGuid().ToString("N");

        lock(_sync) {
            _entries[key] = new ClaimCheckPayload(payload.ToArray(), contentType);
        }

        return Task.FromResult(key);
    }

    /// <inheritdoc />
    public Task<ClaimCheckPayload?> GetAsync(string key, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);

        lock(_sync) {
            return Task.FromResult(_entries.TryGetValue(key, out var payload) ? payload : null);
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);

        lock(_sync) {
            _entries.Remove(key);
        }

        return Task.CompletedTask;
    }
}