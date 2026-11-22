//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Retention;

/// <summary>
/// Hosted retention sweeper. It resolves the policy by store name, takes a lease, asks every
/// configured store for one batch, and records per-store metrics. A failing store never prevents
/// the other stores from running; the next tick retries it.
/// </summary>
/// <remarks>
/// Dry-run is the default in <see cref="RetentionOptions"/>. The in-memory lease protects one
/// host; replace <see cref="IRetentionLease"/> with a durable implementation before running
/// several hosts against one database.
/// </remarks>
public sealed class RetentionSweeper : BackgroundService {
    private static readonly Action<ILogger, string, Exception?> _storeFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(1, "RetentionStoreFailed"), "Retention sweep failed for store {Store}.");
    private static readonly Action<ILogger, Exception?> _loopFailed =
        LoggerMessage.Define(LogLevel.Warning, new EventId(2, "RetentionLoopFailed"), "Retention sweep loop failed.");
    private readonly IRetentionStore[] _stores;
    private readonly RetentionOptions _options;
    private readonly IRetentionLease _lease;
    private readonly RetentionMetrics _metrics;
    private readonly TimeProvider _clock;
    private readonly ILogger<RetentionSweeper>? _logger;
    private readonly Lock _sync = new();
    private DateTimeOffset? _lastAttemptUtc;
    private DateTimeOffset? _lastSuccessUtc;
    private string? _lastError;
    private bool _validated;

    /// <summary>Initializes the sweeper.</summary>
    /// <param name="stores">Registered retention adapters.</param>
    /// <param name="options">Sweeper interval, batch, lease, dry-run, and policies.</param>
    /// <param name="lease">The sweep lease; replace the default for multi-host safety.</param>
    /// <param name="metrics">Per-store retention instruments.</param>
    /// <param name="clock">The clock used for cutoffs and the loop.</param>
    /// <param name="logger">Optional logger for store failures.</param>
    public RetentionSweeper(
        IEnumerable<IRetentionStore> stores,
        IOptions<RetentionOptions> options,
        IRetentionLease lease,
        RetentionMetrics metrics,
        TimeProvider clock,
        ILogger<RetentionSweeper>? logger = null) {
        _stores = (stores ?? throw new ArgumentNullException(nameof(stores))).ToArray();
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _lease = lease ?? throw new ArgumentNullException(nameof(lease));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger;
    }

    /// <summary>Gets the unique owner id used by the sweep lease.</summary>
    public string Owner { get; } = Guid.NewGuid().ToString("N");

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if(!_options.Enabled || _options.Interval <= TimeSpan.Zero) {
            return;
        }

        while(!stoppingToken.IsCancellationRequested) {
            try {
                await SweepOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {
                return;
            }
            catch(Exception exception) {
                RecordFailure(exception);
            }

            try {
                await _clock.DelayAsync(_options.Interval, stoppingToken).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {
                return;
            }
        }
    }

    /// <summary>
    /// Runs one sweep under the lease. Exposed separately so operators and tests can drive a
    /// deterministic pass without waiting for the hosted interval.
    /// </summary>
    /// <param name="cancellationToken">Stops the sweep between stores.</param>
    /// <returns>The per-store results, excluding stores with no enabled policy.</returns>
    public async Task<IReadOnlyList<RetentionBatchResult>> SweepOnceAsync(CancellationToken cancellationToken = default) {
        if(!_options.Enabled || _options.Policies.Count is 0) {
            return [];
        }

        if(!_validated) {
            _options.Validate();
            _validated = true;
        }

        var now = _clock.GetUtcNow();

        if(!await _lease.TryAcquireAsync(Owner, _options.LeaseDuration, now, cancellationToken).ConfigureAwait(false)) {
            return [];
        }

        var results = new List<RetentionBatchResult>();
        Exception? failure = null;

        try {
            foreach(var store in _stores) {
                cancellationToken.ThrowIfCancellationRequested();

                if(!_options.Policies.TryGetValue(store.Name, out var policy) || !policy.Enabled) {
                    continue;
                }

                try {
                    var result = await store
                        .SweepAsync(policy, now, _options.BatchSize, _options.DryRun, cancellationToken)
                        .ConfigureAwait(false);

                    results.Add(result);
                    _metrics.Record(result);
                }
                catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
                    throw;
                }
                catch(Exception exception) {
                    failure = exception;
                    _metrics.RecordFault(store.Name);

                    if(_logger is not null) {
                        _storeFailed(_logger, store.Name, exception);
                    }
                }
            }

            MarkAttempt(now, failure);
        }
        finally {
            await _lease.ReleaseAsync(Owner, CancellationToken.None).ConfigureAwait(false);
        }

        return results;
    }

    /// <summary>Captures the last attempt, last success, and last failure.</summary>
    /// <returns>The current snapshot.</returns>
    public RetentionSweeperSnapshot Snapshot() {
        lock(_sync) {
            return new RetentionSweeperSnapshot(
                _options.Enabled && _options.Interval > TimeSpan.Zero && _options.Policies.Count > 0,
                _options.Policies.Count,
                _lastAttemptUtc,
                _lastSuccessUtc,
                _lastError);
        }
    }

    private void MarkAttempt(DateTimeOffset now, Exception? failure) {
        lock(_sync) {
            _lastAttemptUtc = now;

            if(failure is null) {
                _lastSuccessUtc = now;
                _lastError = null;
            } else {
                _lastError = failure.Message;
            }
        }
    }

    private void RecordFailure(Exception exception) {
        lock(_sync) {
            _lastError = exception.Message;
        }

        if(_logger is not null) {
            _loopFailed(_logger, exception);
        }
    }
}
