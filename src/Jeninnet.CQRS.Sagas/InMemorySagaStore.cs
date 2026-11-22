//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas;

/// <summary>
/// In-memory <see cref="ISagaStore"/>: the default for tests and single-process hosts. State is
/// serialized through <see cref="IMessageSerializer"/> on every save and deserialized on every
/// load, so a failed transition never mutates the stored copy and a custom serializer behaves the
/// same as with the EF store.
/// </summary>
/// <param name="serializer">The state serializer. Never <see langword="null"/>.</param>
/// <param name="clock">Stamps the row update time; defaults to the system clock.</param>
public sealed class InMemorySagaStore(IMessageSerializer serializer, TimeProvider? clock = null) : ISagaStore {
    private readonly IMessageSerializer _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Lock _sync = new();
    private readonly Dictionary<Key, Entry> _entries = [];

    /// <inheritdoc />
    public Task<SagaRecord?> LoadAsync(string sagaId, Type stateType, CancellationToken cancellationToken = default) {
        ValidateKey(sagaId, stateType);

        Entry? entry;
        lock(_sync) {
            _entries.TryGetValue(new Key(stateType, sagaId), out entry);
        }

        if(entry is null) {
            return Task.FromResult<SagaRecord?>(null);
        }

        var state = _serializer.Deserialize(entry.State, stateType) as ISagaState
            ?? throw new InvalidOperationException($"The stored saga state for '{stateType.FullName}' did not deserialize to {nameof(ISagaState)}.");

        return Task.FromResult<SagaRecord?>(new SagaRecord(sagaId, stateType, state, entry.Version, entry.Progress));
    }

    /// <inheritdoc />
    public Task SaveAsync(
        string sagaId,
        Type stateType,
        ISagaState state,
        long expectedVersion,
        SagaProgress progress,
        CancellationToken cancellationToken = default) {
        ValidateKey(sagaId, stateType);
        Guard.NotNull(state);
        Guard.NotNull(progress);

        if(expectedVersion < 0) {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion), expectedVersion, "Expected version must not be negative.");
        }

        var bytes = _serializer.Serialize(state, stateType);
        var storedProgress = Copy(progress);
        var key = new Key(stateType, sagaId);
        var now = _clock.GetUtcNow();

        lock(_sync) {
            if(expectedVersion is 0) {
                if(_entries.TryGetValue(key, out var existing)) {
                    throw new SagaConcurrencyException(sagaId, stateType, expectedVersion, existing.Version);
                }

                _entries[key] = new Entry(bytes, 1, storedProgress, now);

                return Task.CompletedTask;
            }

            if(!_entries.TryGetValue(key, out var current)) {
                throw new SagaConcurrencyException(sagaId, stateType, expectedVersion, -1);
            }

            if(current.Version != expectedVersion) {
                throw new SagaConcurrencyException(sagaId, stateType, expectedVersion, current.Version);
            }

            _entries[key] = new Entry(bytes, expectedVersion + 1, storedProgress, now);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(string sagaId, Type stateType, CancellationToken cancellationToken = default) {
        ValidateKey(sagaId, stateType);

        lock(_sync) {
            _entries.Remove(new Key(stateType, sagaId));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<long> CountAsync(Type stateType, CancellationToken cancellationToken = default) {
        Guard.NotNull(stateType);

        lock(_sync) {
            var count = _entries.Keys.LongCount(key => key.StateType == stateType);

            return Task.FromResult(count);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SagaSnapshot>> ListAsync(Type stateType, CancellationToken cancellationToken = default) {
        Guard.NotNull(stateType);

        lock(_sync) {
            var entries = _entries
                .Where(pair => pair.Key.StateType == stateType)
                .Select(pair => {
                    var state = _serializer.Deserialize(pair.Value.State, stateType) as ISagaState;

                    return new SagaSnapshot(pair.Key.SagaId, stateType, state?.CurrentState ?? string.Empty, pair.Value.UpdatedAtUtc);
                })
                .ToList();

            return Task.FromResult<IReadOnlyList<SagaSnapshot>>(entries);
        }
    }

    private static SagaProgress Copy(SagaProgress progress) =>
        new([.. progress.CompletedSteps], new HashSet<string>(progress.CompensatedSteps, StringComparer.Ordinal));

    private static void ValidateKey(string sagaId, Type stateType) {
        Guard.NotNullOrWhiteSpace(sagaId);
        Guard.NotNull(stateType);
    }

    private readonly record struct Key(Type StateType, string SagaId);

    private sealed record Entry(byte[] State, long Version, SagaProgress Progress, DateTimeOffset UpdatedAtUtc);
}