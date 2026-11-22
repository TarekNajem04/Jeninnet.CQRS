//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Outbox;

/// <summary>
/// Hosted transport relay: leases a batch of outbox rows, publishes them through
/// <typeparamref name="TTransport"/>, and completes each row only after the broker accepts it.
/// </summary>
/// <remarks>
/// Delivery is at-least-once. A publish failure increments the row's attempt count and schedules
/// an exponential backoff; at <see cref="OutboxRelayOptions.MaxAttempts"/> the row moves to
/// <see cref="IDeadLetterStore"/> with its last error. Rows are processed in staged order; with
/// <see cref="OutboxRelayOptions.StopOnFailure"/> the batch stops at the first failure so later
/// rows cannot overtake it. Leases are persisted, so two workers on one store cannot both own the
/// same row.
/// </remarks>
/// <typeparam name="TTransport">The registered transport implementation used as the metric tag.</typeparam>
public sealed class OutboxRelayWorker<TTransport> : BackgroundService
    where TTransport : class, IMessageTransport {
    private readonly IOutboxStore _store;
    private readonly TTransport _transport;
    private readonly IDeadLetterStore _deadLetters;
    private readonly OutboxRelayOptions _options;
    private readonly TimeProvider _clock;
    private readonly OutboxRelayMetrics _metrics;
    private readonly TenantAccessor? _tenant;
    private readonly string _owner;

    /// <summary>Initializes the relay worker.</summary>
    /// <param name="store">The leased outbox store. Never <see langword="null"/>.</param>
    /// <param name="transport">The transport that accepts each row. Never <see langword="null"/>.</param>
    /// <param name="deadLetters">Receives rows that reach the poison threshold. Never <see langword="null"/>.</param>
    /// <param name="options">Batch, lease, and retry options. Never <see langword="null"/>.</param>
    /// <param name="clock">The clock used for leases and backoff. Never <see langword="null"/>.</param>
    /// <param name="metrics">The relay instruments. Never <see langword="null"/>.</param>
    /// <param name="tenant">The ambient tenant adapter; <see langword="null"/> without tenancy.</param>
    /// <exception cref="ArgumentOutOfRangeException">An option is outside its documented range.</exception>
    public OutboxRelayWorker(
        IOutboxStore store,
        TTransport transport,
        IDeadLetterStore deadLetters,
        IOptions<OutboxRelayOptions> options,
        TimeProvider clock,
        OutboxRelayMetrics metrics,
        TenantAccessor? tenant = null) {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _deadLetters = deadLetters ?? throw new ArgumentNullException(nameof(deadLetters));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _tenant = tenant;
        _options = Validate((options ?? throw new ArgumentNullException(nameof(options))).Value);
        _owner = StringCompat.IsNullOrWhiteSpace(_options.OwnerId)
            ? $"{Environment.MachineName}:{ProcessIdentity.Id}:{Guid.NewGuid():N}"
            : _options.OwnerId;
    }

    /// <summary>Gets the lease owner id used by this worker.</summary>
    public string Owner => _owner;

    /// <summary>Gets the diagnostic transport name for this worker.</summary>
    public string TransportName => typeof(TTransport).Name;

    /// <summary>
    /// Leases and publishes one batch. Exposed separately so tests and operators can drive a poll
    /// with a fake clock without starting the hosted loop.
    /// </summary>
    /// <param name="cancellationToken">Stops the batch between rows.</param>
    public async Task<OutboxRelayBatchResult> RelayOnceAsync(CancellationToken cancellationToken = default) { // NOSONAR: Complexity is inherent to this state machine/pipeline and is covered by tests.
        var leases = await _store.LeaseAsync(
            _owner,
            _options.BatchSize,
            _options.LeaseDuration,
            _clock.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);

        if(leases.Count is 0) {
            return new OutboxRelayBatchResult(0, 0, 0, 0);
        }

        var published = 0;
        var retried = 0;
        var deadLettered = 0;

        for(var index = 0; index < leases.Count; index++) {
            if(cancellationToken.IsCancellationRequested) {
                await ReleaseAsync(leases, index, CancellationToken.None).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var lease = leases[index];
            var command = lease.Envelope.Command;
            var messageType = command.GetType();
            using var tenantScope = _tenant?.EnterScope(lease.Envelope.TenantId);
            var tenantId = _tenant?.Resolve(lease.Envelope.TenantId);

            try {
                await _transport.PublishAsync(
                    command,
                    messageType,
                    new TransportMessageOptions {
                        MessageId = lease.Key,
                        TenantId = tenantId,
                    },
                    cancellationToken).ConfigureAwait(false);
                await _store.CompleteLeaseAsync(lease.Key, _owner, _clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
                _metrics.RecordPublished(TransportName, messageType);
                published++;
            }
            catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
                await ReleaseAsync(leases, index, CancellationToken.None).ConfigureAwait(false);

                throw;
            }
            catch(Exception exception) {
                var attempts = lease.Attempts + 1;
                var poison = attempts >= _options.MaxAttempts;
                _metrics.RecordFailed(TransportName, messageType, poison);

                if(poison) {
                    try {
                        await _deadLetters.RecordAsync(lease.Envelope, exception, _clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
                        await _store.CompleteLeaseAsync(lease.Key, _owner, _clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
                        deadLettered++;
                    }
                    catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
                        await ReleaseAsync(leases, index, CancellationToken.None).ConfigureAwait(false);

                        throw;
                    }
                    catch {
                        // The dead-letter write failed; keep the row pending under backoff.
                        await FailAsync(lease, attempts, exception, cancellationToken).ConfigureAwait(false);
                        retried++;
                    }
                } else {
                    await FailAsync(lease, attempts, exception, cancellationToken).ConfigureAwait(false);
                    retried++;
                }

                if(_options.StopOnFailure) {
                    await ReleaseAsync(leases, index + 1, cancellationToken).ConfigureAwait(false);

                    break;
                }
            }
        }

        return new OutboxRelayBatchResult(leases.Count, published, retried, deadLettered);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while(!stoppingToken.IsCancellationRequested) {
            try {
                await RelayOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {
                return;
            }
            catch(Exception) {
                // A store or transport outage must not kill the host; the next poll retries.
            }

            try {
                await _clock.DelayAsync(_options.PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {
                return;
            }
        }
    }

    private async Task FailAsync(OutboxLease lease, int attempts, Exception exception, CancellationToken cancellationToken) {
        var now = _clock.GetUtcNow();

        await _store.FailLeaseAsync(
            lease.Key,
            _owner,
            exception,
            attempts,
            now + NextDelay(attempts),
            now,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ReleaseAsync(IReadOnlyList<OutboxLease> leases, int startIndex, CancellationToken cancellationToken) {
        for(var index = startIndex; index < leases.Count; index++) {
            try {
                await _store.ReleaseLeaseAsync(leases[index].Key, _owner, _clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            }
            catch(Exception) {
                // The lease expires on its own; a failed release must not mask the real error.
            }
        }
    }

    private TimeSpan NextDelay(int attempts) {
        var seconds = _options.InitialRetryDelay.TotalSeconds * Math.Pow(_options.BackoffMultiplier, attempts - 1);

        return TimeSpan.FromSeconds(Math.Min(seconds, _options.MaxRetryDelay.TotalSeconds));
    }

    private static OutboxRelayOptions Validate(OutboxRelayOptions options) {
        Guard.NotNull(options);

        if(options.BatchSize < 1) {
            throw new ArgumentOutOfRangeException(nameof(options), options.BatchSize, "BatchSize must be at least 1.");
        }

        if(options.PollInterval <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options), options.PollInterval, "PollInterval must be greater than zero.");
        }

        if(options.LeaseDuration <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options), options.LeaseDuration, "LeaseDuration must be greater than zero.");
        }

        if(options.MaxAttempts < 1) {
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxAttempts, "MaxAttempts must be at least 1.");
        }

        if(options.InitialRetryDelay <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options), options.InitialRetryDelay, "InitialRetryDelay must be greater than zero.");
        }

        if(options.MaxRetryDelay < options.InitialRetryDelay) {
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxRetryDelay, "MaxRetryDelay must be at least InitialRetryDelay.");
        }

        if(options.BackoffMultiplier < 1) {
            throw new ArgumentOutOfRangeException(nameof(options), options.BackoffMultiplier, "BackoffMultiplier must be at least 1.");
        }

        return options;
    }
}
