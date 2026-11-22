//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.DeadLetters;

/// <summary>
/// In-memory <see cref="IDeadLetterStore"/>: the default. Entries live only as long as the
/// process; use the EF Core store for restart safety. Singleton-safe.
/// </summary>
public sealed class InMemoryDeadLetterStore : IDeadLetterStore {
    private readonly Lock _sync = new();
    private readonly Dictionary<string, DeadLetterEntry> _entries = new(StringComparer.Ordinal);
    private readonly DeadLetterOptions _options;

    /// <summary>
    /// Initializes the store from options.
    /// </summary>
    /// <param name="options">The retention options; never <see langword="null"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <see cref="DeadLetterOptions.MaxEntries"/> is less than 1 or
    /// <see cref="DeadLetterOptions.Retention"/> is negative.
    /// </exception>
    public InMemoryDeadLetterStore(IOptions<DeadLetterOptions> options) {
        Guard.NotNull(options);
        _options = options.Value;

        if(_options.MaxEntries < 1) {
            throw new ArgumentOutOfRangeException(nameof(options), _options.MaxEntries, "MaxEntries must be at least 1.");
        }

        if(_options.Retention < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options), _options.Retention, "Retention must not be negative.");
        }
    }

    /// <inheritdoc />
    public Task<string> RecordAsync(IDeferredEnvelope envelope, Exception error, DateTimeOffset failedAtUtc, CancellationToken cancellationToken = default) {
        Guard.NotNull(envelope);
        Guard.NotNull(error);

        var key = Guid.NewGuid().ToString("N");
        var entry = new DeadLetterEntry(key, envelope, 1, DeadLetterEntry.ErrorTypeOf(error), error.Message, failedAtUtc, failedAtUtc);

        lock(_sync) {
            _entries[key] = entry;
            TrimLocked();
        }

        return Task.FromResult(key);
    }

    /// <inheritdoc />
    public Task RecordFailureAsync(string key, Exception error, DateTimeOffset failedAtUtc, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);
        Guard.NotNull(error);

        lock(_sync) {
            if(_entries.TryGetValue(key, out var entry)) {
                _entries[key] = entry.FailedAgain(error, failedAtUtc);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DeadLetterEntry>> ListAsync(CancellationToken cancellationToken = default) {
        lock(_sync) {
            var listed = _entries.Values
                .OrderByDescending(static entry => entry.LastFailedAtUtc)
                .ThenBy(static entry => entry.Key, StringComparer.Ordinal)
                .ToList();

            return Task.FromResult<IReadOnlyList<DeadLetterEntry>>(listed);
        }
    }

    /// <inheritdoc />
    public Task<DeadLetterEntry?> GetAsync(string key, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);

        lock(_sync) {
            return Task.FromResult(_entries.TryGetValue(key, out var entry) ? entry : null);
        }
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);

        lock(_sync) {
            _entries.Remove(key);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> PruneAsync(DateTimeOffset now, CancellationToken cancellationToken = default) {
        var cutoff = now - _options.Retention;
        List<string> expired;

        lock(_sync) {
            expired = _entries
                .Where(pair => pair.Value.LastFailedAtUtc < cutoff)
                .Select(static pair => pair.Key)
                .ToList();

            foreach(var key in expired) {
                _entries.Remove(key);
            }

            TrimLocked();
        }

        return Task.FromResult(expired.Count);
    }

    private void TrimLocked() {
        while(_entries.Count > _options.MaxEntries) {
            string? oldestKey = null;
            var oldestAt = DateTimeOffset.MaxValue;

            foreach(var pair in _entries) {
                if(pair.Value.LastFailedAtUtc < oldestAt) {
                    oldestAt = pair.Value.LastFailedAtUtc;
                    oldestKey = pair.Key;
                }
            }

            if(oldestKey is null) {
                return;
            }

            _entries.Remove(oldestKey);
        }
    }
}
