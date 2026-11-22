//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.RoutingSlips;

/// <summary>
/// Process-local <see cref="IRoutingSlipStore"/>: the default for tests and single-process hosts.
/// Every operation copies the row and its lists, so a caller cannot mutate persisted progress.
/// </summary>
public sealed class InMemoryRoutingSlipStore : IRoutingSlipStore {
    private readonly Lock _sync = new();
    private readonly Dictionary<string, RoutingSlipRecord> _rows = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<RoutingSlipRecord> CreateAsync(RoutingSlipRecord record, CancellationToken cancellationToken = default) {
        Validate(record);

        lock(_sync) {
            if(_rows.ContainsKey(record.SlipId)) {
                throw new RoutingSlipConcurrencyException(record.SlipId, 0, 1);
            }

            var stored = Copy(record) with { Version = 1 };
            _rows[stored.SlipId] = stored;

            return Task.FromResult(Copy(stored));
        }
    }

    /// <inheritdoc />
    public Task<RoutingSlipRecord?> LoadAsync(string slipId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(slipId);

        lock(_sync) {
            return Task.FromResult(_rows.TryGetValue(slipId, out var record) ? Copy(record) : null);
        }
    }

    /// <inheritdoc />
    public Task<RoutingSlipRecord> SaveAsync(RoutingSlipRecord record, long expectedVersion, CancellationToken cancellationToken = default) {
        Validate(record);

        if(expectedVersion < 0) {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion), expectedVersion, "Expected version must not be negative.");
        }

        lock(_sync) {
            if(!_rows.TryGetValue(record.SlipId, out var current)) {
                throw new RoutingSlipConcurrencyException(record.SlipId, expectedVersion, -1);
            }

            if(current.Version != expectedVersion) {
                throw new RoutingSlipConcurrencyException(record.SlipId, expectedVersion, current.Version);
            }

            var stored = Copy(record) with { Version = expectedVersion + 1 };
            _rows[stored.SlipId] = stored;

            return Task.FromResult(Copy(stored));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RoutingSlipSnapshot>> ListAsync(string? definitionName = null, CancellationToken cancellationToken = default) {
        lock(_sync) {
            var listed = _rows.Values
                .Where(record => StringCompat.IsNullOrWhiteSpace(definitionName) ||
                                 string.Equals(record.DefinitionName, definitionName, StringComparison.Ordinal))
                .OrderByDescending(static record => record.UpdatedAtUtc)
                .ThenBy(static record => record.SlipId, StringComparer.Ordinal)
                .Select(static record => new RoutingSlipSnapshot(
                    record.SlipId,
                    record.DefinitionName,
                    record.Status,
                    record.NextStepIndex,
                    [.. record.CompletedSteps],
                    [.. record.CompensatedSteps],
                    record.FailedStep,
                    record.CompensationErrorMessage ?? record.FailureErrorMessage,
                    record.UpdatedAtUtc))
                .ToList();

            return Task.FromResult<IReadOnlyList<RoutingSlipSnapshot>>(listed);
        }
    }

    /// <inheritdoc />
    public Task<long> CountAsync(RoutingSlipStatus status, CancellationToken cancellationToken = default) {
        lock(_sync) {
            return Task.FromResult(_rows.Values.LongCount(record => record.Status == status));
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(string slipId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(slipId);

        lock(_sync) {
            _rows.Remove(slipId);
        }

        return Task.CompletedTask;
    }

    private static void Validate(RoutingSlipRecord record) {
        Guard.NotNull(record);
        Guard.NotNullOrWhiteSpace(record.SlipId);
        Guard.NotNullOrWhiteSpace(record.DefinitionName);
    }

    private static RoutingSlipRecord Copy(RoutingSlipRecord record) =>
        record with {
            ArgumentsPayload = [.. record.ArgumentsPayload],
            CompletedSteps = [.. record.CompletedSteps],
            CompensatedSteps = [.. record.CompensatedSteps],
        };
}
