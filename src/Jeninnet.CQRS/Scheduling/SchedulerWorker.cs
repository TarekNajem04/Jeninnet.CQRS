//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Scheduling;

/// <summary>Headers added to scheduled messages.</summary>
public static class SchedulerHeaders {
    /// <summary>Header carrying the schedule key.</summary>
    public const string Key = "x-cqrs-schedule-key";
}

/// <summary>Outcome of one scheduler poll.</summary>
/// <param name="Leased">Rows leased.</param>
/// <param name="Fired">Rows published successfully.</param>
/// <param name="Retried">Rows left pending after a failed publish.</param>
public readonly record struct SchedulerBatchResult(int Leased, int Fired, int Retried);

/// <summary>
/// Hosted persistent scheduler worker. It leases due rows, publishes through
/// <see cref="IMessageTransport"/>, deletes one-shot rows, and re-arms recurring rows from their
/// cron expression. A failed publish is retried with exponential backoff.
/// </summary>
public sealed class SchedulerWorker : BackgroundService {
    private readonly ISchedulerStore _store;
    private readonly IMessageTransport _transport;
    private readonly IMessageSerializerRegistry _serializers;
    private readonly SchedulerOptions _options;
    private readonly TimeProvider _clock;
    private readonly SchedulerMetrics _metrics;
    private readonly TenantAccessor? _tenant;
    private readonly string _owner;

    /// <summary>Initializes the worker.</summary>
    /// <param name="store">The persistent schedule store.</param>
    /// <param name="transport">The transport used to publish due rows.</param>
    /// <param name="serializers">The content-type registry.</param>
    /// <param name="options">Poll/lease/retry options.</param>
    /// <param name="clock">The clock used for due/backoff.</param>
    /// <param name="metrics">The scheduler instruments.</param>
    /// <param name="tenant">The ambient tenant adapter; <see langword="null"/> without tenancy.</param>
    public SchedulerWorker(
        ISchedulerStore store,
        IMessageTransport transport,
        IMessageSerializerRegistry serializers,
        IOptions<SchedulerOptions> options,
        TimeProvider clock,
        SchedulerMetrics metrics,
        TenantAccessor? tenant = null) {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _serializers = serializers ?? throw new ArgumentNullException(nameof(serializers));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _tenant = tenant;
        _options = Validate((options ?? throw new ArgumentNullException(nameof(options))).Value);
        _owner = StringCompat.IsNullOrWhiteSpace(_options.OwnerId)
            ? $"{Environment.MachineName}:{ProcessIdentity.Id}:{Guid.NewGuid():N}"
            : _options.OwnerId;
    }

    /// <summary>Gets the lease owner id.</summary>
    public string Owner => _owner;

    /// <summary>Leases and processes one due batch; exposed for tests and operators.</summary>
    /// <param name="cancellationToken">Stops between rows.</param>
    public async Task<SchedulerBatchResult> RunOnceAsync(CancellationToken cancellationToken = default) {
        var leases = await _store.LeaseDueAsync(
            _owner,
            _options.BatchSize,
            _clock.GetUtcNow(),
            _options.LeaseDuration,
            cancellationToken).ConfigureAwait(false);

        if(leases.Count is 0) {
            return new SchedulerBatchResult(0, 0, 0);
        }

        var fired = 0;
        var retried = 0;

        foreach(var lease in leases) {
            if(cancellationToken.IsCancellationRequested) {
                break;
            }

            var outcome = await ProcessLeaseAsync(lease, cancellationToken).ConfigureAwait(false);

            if(outcome) {
                fired++;
            } else {
                retried++;
            }
        }

        return new SchedulerBatchResult(leases.Count, fired, retried);
    }

    /// <summary>
    /// Publishes one leased row and updates the store. Exposed so an in-flight cancellation test
    /// can retain a lease, cancel the schedule, and still observe the current execution.
    /// </summary>
    /// <param name="lease">The leased row.</param>
    /// <param name="cancellationToken">Cancels the publish.</param>
    /// <returns><see langword="true"/> when the row fired; <see langword="false"/> when it was retried.</returns>
    public async Task<bool> ProcessLeaseAsync(SchedulerLease lease, CancellationToken cancellationToken = default) {
        Guard.NotNull(lease);

        using var tenantScope = _tenant?.EnterScope(lease.Message.TenantId);

        try {
            var messageType = Type.GetType(lease.Message.MessageType, throwOnError: false)
                ?? throw new SchedulerMessageTypeException(lease.Message.MessageType, lease.Message.Key);
            var message = _serializers.Deserialize(lease.Message.Payload, messageType, lease.Message.ContentType)
                ?? throw new InvalidOperationException($"Scheduled message '{lease.Message.Key}' did not deserialize.");

            await _transport.PublishAsync(
                message,
                messageType,
                new TransportMessageOptions {
                    TenantId = _tenant?.Resolve(lease.Message.TenantId),
                    Headers = new Dictionary<string, string>(StringComparer.Ordinal) {
                        [SchedulerHeaders.Key] = lease.Message.Key,
                    },
                },
                cancellationToken).ConfigureAwait(false);

            _metrics.RecordFired(lease.Message.Kind, messageType.Name);

            if(lease.Message.Kind is ScheduledMessageKind.OneShot) {
                await _store.CompleteAsync(lease.Message.Key, lease.Owner, lease.Message.Version, cancellationToken).ConfigureAwait(false);
            } else {
                var cron = CronExpression.Parse(lease.Message.CronExpression
                    ?? throw new InvalidOperationException($"Recurring schedule '{lease.Message.Key}' has no cron expression."));
                var timeZone = StringCompat.IsNullOrWhiteSpace(lease.Message.TimeZoneId)
                    ? TimeZoneInfo.Utc
                    : TimeZoneInfo.FindSystemTimeZoneById(lease.Message.TimeZoneId);
                var next = cron.GetNextOccurrence(lease.Message.NextRunUtc, timeZone);
                await _store.ScheduleNextAsync(lease.Message.Key, lease.Owner, lease.Message.Version, next, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch(Exception exception) {
            var attempts = lease.Message.Attempts + 1;
            _metrics.RecordFailed(lease.Message.Kind, lease.Message.MessageType);
            await _store.FailAsync(
                lease.Message.Key,
                lease.Owner,
                lease.Message.Version,
                exception,
                attempts,
                _clock.GetUtcNow() + NextDelay(attempts),
                cancellationToken).ConfigureAwait(false);

            return false;
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while(!stoppingToken.IsCancellationRequested) {
            try {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
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

    private TimeSpan NextDelay(int attempts) {
        var seconds = _options.InitialRetryDelay.TotalSeconds * Math.Pow(_options.BackoffMultiplier, attempts - 1);

        return TimeSpan.FromSeconds(Math.Min(seconds, _options.MaxRetryDelay.TotalSeconds));
    }

    private static SchedulerOptions Validate(SchedulerOptions options) {
        if(options.BatchSize < 1) {
            throw new ArgumentOutOfRangeException(nameof(options), options.BatchSize, "BatchSize must be at least 1.");
        }

        if(options.PollInterval <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options), options.PollInterval, "PollInterval must be greater than zero.");
        }

        if(options.LeaseDuration <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options), options.LeaseDuration, "LeaseDuration must be greater than zero.");
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