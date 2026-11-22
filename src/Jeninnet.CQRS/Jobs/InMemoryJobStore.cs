//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Jobs;

/// <summary>
/// Process-local <see cref="IJobStore"/>: the default for tests and single-process hosts.
/// Every operation copies the row, so callers cannot mutate persisted state.
/// </summary>
/// <param name="clock">Stamps progress and lease updates; defaults to the system clock.</param>
public sealed class InMemoryJobStore(TimeProvider? clock = null) : IJobStore {
    private readonly Lock _sync = new();
    private readonly Dictionary<string, JobRecord> _rows = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <inheritdoc />
    public Task<JobRecord> CreateAsync(JobRecord record, CancellationToken cancellationToken = default) {
        Validate(record);

        lock(_sync) {
            if(_rows.ContainsKey(record.JobId)) {
                throw new InvalidOperationException($"A job with id '{record.JobId}' already exists.");
            }

            var stored = Copy(record) with { Version = 1 };
            _rows[stored.JobId] = stored;

            return Task.FromResult(Copy(stored));
        }
    }

    /// <inheritdoc />
    public Task<JobRecord?> GetAsync(string jobId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(jobId);

        lock(_sync) {
            return Task.FromResult(_rows.TryGetValue(jobId, out var record) ? Copy(record) : null);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<JobRecord>> LeaseDueAsync(
        string owner,
        int batchSize,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(owner);

        if(batchSize < 1) {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be at least 1.");
        }

        if(leaseDuration <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), leaseDuration, "Lease duration must be greater than zero.");
        }

        var leased = new List<JobRecord>();

        lock(_sync) {
            var due = _rows.Values
                .Where(record =>
                    (record.Status is JobStatus.Queued &&
                     !record.CancellationRequested &&
                     record.NextAttemptUtc <= now) ||
                    (record.Status is JobStatus.Running &&
                     record.LeaseExpiresAtUtc is { } expiresAtUtc &&
                     expiresAtUtc <= now))
                .OrderBy(static record => record.NextAttemptUtc)
                .ThenBy(static record => record.CreatedAtUtc)
                .Take(batchSize)
                .ToList();

            foreach(var record in due) {
                var updated = record with {
                    Status = JobStatus.Running,
                    Attempts = record.Attempts + 1,
                    StartedAtUtc = record.StartedAtUtc ?? now,
                    LeaseOwner = owner,
                    LeaseExpiresAtUtc = now + leaseDuration,
                    UpdatedAtUtc = now,
                    Version = record.Version + 1,
                };
                _rows[updated.JobId] = updated;
                leased.Add(Copy(updated));
            }
        }

        return Task.FromResult<IReadOnlyList<JobRecord>>(leased);
    }

    /// <inheritdoc />
    public Task<bool> ReportProgressAsync(string jobId, string owner, int progress, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(jobId);
        Guard.NotNullOrWhiteSpace(owner);

        lock(_sync) {
            if(!_rows.TryGetValue(jobId, out var record) ||
               record.Status is not JobStatus.Running ||
               !string.Equals(record.LeaseOwner, owner, StringComparison.Ordinal)) {
                return Task.FromResult(false);
            }

            var clamped = progress < 0 ? 0 : progress > 100 ? 100 : progress;
            _rows[jobId] = record with {
                Progress = clamped,
                UpdatedAtUtc = _clock.GetUtcNow(),
                Version = record.Version + 1,
            };

            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> RenewLeaseAsync(string jobId, string owner, TimeSpan leaseDuration, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(jobId);
        Guard.NotNullOrWhiteSpace(owner);

        if(leaseDuration <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), leaseDuration, "Lease duration must be greater than zero.");
        }

        lock(_sync) {
            if(!_rows.TryGetValue(jobId, out var record) ||
               record.Status is not JobStatus.Running ||
               !string.Equals(record.LeaseOwner, owner, StringComparison.Ordinal)) {
                return Task.FromResult(false);
            }

            var now = _clock.GetUtcNow();
            _rows[jobId] = record with {
                LeaseExpiresAtUtc = now + leaseDuration,
                UpdatedAtUtc = now,
                Version = record.Version + 1,
            };

            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> RequestCancellationAsync(string jobId, DateTimeOffset now, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(jobId);

        lock(_sync) {
            if(!_rows.TryGetValue(jobId, out var record)) {
                return Task.FromResult(false);
            }

            if(record.IsTerminal) {
                return Task.FromResult(true);
            }

            _rows[jobId] = record.Status is JobStatus.Queued
                ? record with {
                    Status = JobStatus.Canceled,
                    CancellationRequested = true,
                    CompletedAtUtc = now,
                    UpdatedAtUtc = now,
                    Version = record.Version + 1,
                }
                : record with {
                    CancellationRequested = true,
                    UpdatedAtUtc = now,
                    Version = record.Version + 1,
                };

            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> IsCancellationRequestedAsync(string jobId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(jobId);

        lock(_sync) {
            return Task.FromResult(_rows.TryGetValue(jobId, out var record) && record.CancellationRequested);
        }
    }

    /// <inheritdoc />
    public Task<bool> CompleteAsync(
        string jobId,
        string owner,
        JobStatus status,
        int progress,
        Exception? error,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(jobId);
        Guard.NotNullOrWhiteSpace(owner);

        if(status is not (JobStatus.Succeeded or JobStatus.Failed or JobStatus.Canceled)) {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Only terminal job statuses can be completed.");
        }

        lock(_sync) {
            if(!_rows.TryGetValue(jobId, out var record) || record.IsTerminal) {
                return Task.FromResult(false);
            }

            if(record.Status is not JobStatus.Running ||
               !string.Equals(record.LeaseOwner, owner, StringComparison.Ordinal)) {
                return Task.FromResult(false);
            }

            var clamped = progress < 0 ? 0 : progress > 100 ? 100 : progress;
            _rows[jobId] = record with {
                Status = status,
                Progress = clamped,
                LastErrorType = error is null ? null : ErrorTypeOf(error),
                LastErrorMessage = error?.Message,
                CompletedAtUtc = now,
                UpdatedAtUtc = now,
                LeaseOwner = null,
                LeaseExpiresAtUtc = null,
                Version = record.Version + 1,
            };

            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> RetryAsync(
        string jobId,
        string owner,
        Exception error,
        DateTimeOffset nextAttemptUtc,
        CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(jobId);
        Guard.NotNullOrWhiteSpace(owner);
        Guard.NotNull(error);

        lock(_sync) {
            if(!_rows.TryGetValue(jobId, out var record) ||
               record.Status is not JobStatus.Running ||
               !string.Equals(record.LeaseOwner, owner, StringComparison.Ordinal)) {
                return Task.FromResult(false);
            }

            var now = _clock.GetUtcNow();
            _rows[jobId] = record with {
                Status = JobStatus.Queued,
                NextAttemptUtc = nextAttemptUtc,
                LastErrorType = ErrorTypeOf(error),
                LastErrorMessage = error.Message,
                LeaseOwner = null,
                LeaseExpiresAtUtc = null,
                UpdatedAtUtc = now,
                Version = record.Version + 1,
            };

            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<int> PruneAsync(DateTimeOffset completedBeforeUtc, CancellationToken cancellationToken = default) {
        lock(_sync) {
            var expired = _rows.Values
                .Where(record => record.IsTerminal &&
                                 record.CompletedAtUtc is { } completedAtUtc &&
                                 completedAtUtc <= completedBeforeUtc)
                .Select(static record => record.JobId)
                .ToList();

            foreach(var jobId in expired) {
                _rows.Remove(jobId);
            }

            return Task.FromResult(expired.Count);
        }
    }

    /// <inheritdoc />
    public Task<long> CountAsync(JobStatus status, CancellationToken cancellationToken = default) {
        lock(_sync) {
            return Task.FromResult(_rows.Values.LongCount(record => record.Status == status));
        }
    }

    private static void Validate(JobRecord record) {
        Guard.NotNull(record);
        Guard.NotNullOrWhiteSpace(record.JobId);
        Guard.NotNullOrWhiteSpace(record.JobType);
    }

    private static string ErrorTypeOf(Exception error) =>
        error.GetType().FullName ?? error.GetType().Name;

    private static JobRecord Copy(JobRecord record) =>
        record with { Payload = [.. record.Payload] };
}
