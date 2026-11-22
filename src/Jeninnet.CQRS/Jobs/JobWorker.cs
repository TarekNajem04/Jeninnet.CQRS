//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Jobs;

/// <summary>Outcome counts for one worker poll.</summary>
/// <param name="Leased">Jobs leased.</param>
/// <param name="Succeeded">Jobs completed successfully.</param>
/// <param name="Failed">Jobs that exhausted their attempts.</param>
/// <param name="Retried">Jobs returned to the queue after a failed attempt.</param>
/// <param name="Canceled">Jobs that ended canceled.</param>
public readonly record struct JobRunResult(int Leased, int Succeeded, int Failed, int Retried, int Canceled);

/// <summary>
/// Hosted job worker. It leases due rows, runs the strongly typed handler with a progress
/// reporter, renews the lease while it runs, watches the durable cancellation flag, and applies
/// the retry/backoff policy. Terminal failures are dead-lettered.
/// </summary>
public sealed class JobWorker : BackgroundService {
    private readonly IJobStore _store;
    private readonly IServiceScopeFactory _scopes;
    private readonly JobHandlerRegistry _handlers;
    private readonly IMessageSerializer _serializer;
    private readonly JobOptions _options;
    private readonly TimeProvider _clock;
    private readonly JobMetrics _metrics;
    private readonly IDeadLetterStore _deadLetters;
    private readonly TenantAccessor? _tenant;
    private readonly string _owner;

    /// <summary>Initializes the worker.</summary>
    /// <param name="store">The durable job store.</param>
    /// <param name="scopes">Creates a scope per job attempt.</param>
    /// <param name="handlers">The registered job handlers.</param>
    /// <param name="serializer">Deserializes the job payload.</param>
    /// <param name="options">Poll/lease/retry/cancellation options.</param>
    /// <param name="clock">The clock used for due times and backoff.</param>
    /// <param name="metrics">The job instruments.</param>
    /// <param name="deadLetters">Receives jobs that exhaust their attempts.</param>
    /// <param name="tenant">The ambient tenant adapter; <see langword="null"/> without tenancy.</param>
    public JobWorker(
        IJobStore store,
        IServiceScopeFactory scopes,
        JobHandlerRegistry handlers,
        IMessageSerializer serializer,
        IOptions<JobOptions> options,
        TimeProvider clock,
        JobMetrics metrics,
        IDeadLetterStore deadLetters,
        TenantAccessor? tenant = null) {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _deadLetters = deadLetters ?? throw new ArgumentNullException(nameof(deadLetters));
        _tenant = tenant;
        _options = Validate(options);
        _owner = StringCompat.IsNullOrWhiteSpace(_options.OwnerId)
            ? $"{Environment.MachineName}:{ProcessIdentity.Id}:{Guid.NewGuid():N}"
            : _options.OwnerId;
    }

    /// <summary>Gets the worker lease owner id.</summary>
    public string Owner => _owner;

    /// <summary>Leases and processes one due batch; exposed for tests and operators.</summary>
    /// <param name="cancellationToken">Stops between jobs.</param>
    public async Task<JobRunResult> RunOnceAsync(CancellationToken cancellationToken = default) {
        var leased = await _store.LeaseDueAsync(
            _owner,
            _options.BatchSize,
            _clock.GetUtcNow(),
            _options.LeaseDuration,
            cancellationToken).ConfigureAwait(false);

        if(leased.Count is 0) {
            return new JobRunResult(0, 0, 0, 0, 0);
        }

        // Every leased job starts immediately so each lease heartbeat runs while its handler works.
        var outcomes = await Task.WhenAll(leased.Select(job => ProcessAsync(job, cancellationToken)))
            .ConfigureAwait(false);

        return new JobRunResult(
            leased.Count,
            outcomes.Count(static outcome => outcome == JobOutcome.Succeeded),
            outcomes.Count(static outcome => outcome == JobOutcome.Failed),
            outcomes.Count(static outcome => outcome == JobOutcome.Retried),
            outcomes.Count(static outcome => outcome == JobOutcome.Canceled));
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
                // A store or handler outage must not kill the host; the next poll retries.
            }

            try {
                await _clock.DelayAsync(_options.PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {
                return;
            }
        }
    }

    private async Task<JobOutcome> ProcessAsync(JobRecord job, CancellationToken cancellationToken) { // NOSONAR: Complexity is inherent to this state machine/pipeline and is covered by tests.
        if(cancellationToken.IsCancellationRequested) {
            return JobOutcome.None;
        }

        using var tenantScope = _tenant?.EnterScope(job.TenantId);

        if(job.CancellationRequested) {
            return await CompleteCanceledAsync(job, job.Progress).ConfigureAwait(false);
        }

        if(job.Attempts > job.MaxAttempts) {
            return await FailPermanentAsync(
                job,
                new InvalidOperationException(
                    $"Job '{job.JobId}' exceeded its attempt budget ({job.Attempts}/{job.MaxAttempts}) after a lease expiry."),
                job.Progress).ConfigureAwait(false);
        }

        IJobHandlerInvoker handler;

        try {
            handler = _handlers.GetRequired(job.JobType);
        }
        catch(Exception error) {
            return await FailPermanentAsync(job, error, job.Progress).ConfigureAwait(false);
        }
        using var scope = _scopes.CreateScope();
        using var jobCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var leaseLost = 0;
        var cancellationRequested = 0;
        var leaseTask = MaintainLeaseAsync(job.JobId, jobCancellation, () => Interlocked.Exchange(ref leaseLost, 1), cancellationToken);
        var watchTask = WatchCancellationAsync(job.JobId, jobCancellation, () => Interlocked.Exchange(ref cancellationRequested, 1), cancellationToken);
        var progress = new JobProgress(_store, job.JobId, _owner, job.Progress);
        _metrics.RecordStarted(job.JobType);

        try {
            await handler
                .ExecuteAsync(scope.ServiceProvider, _serializer, job.Payload, progress, jobCancellation.Token)
                .ConfigureAwait(false);

            if(Volatile.Read(ref leaseLost) is 1) {
                return JobOutcome.None;
            }

            if(Volatile.Read(ref cancellationRequested) is 1 ||
               await _store.IsCancellationRequestedAsync(job.JobId, CancellationToken.None).ConfigureAwait(false)) {
                return await CompleteCanceledAsync(job, progress.Current).ConfigureAwait(false);
            }

            var completed = await _store
                .CompleteAsync(job.JobId, _owner, JobStatus.Succeeded, 100, null, _clock.GetUtcNow(), CancellationToken.None)
                .ConfigureAwait(false);

            if(completed) {
                _metrics.RecordSucceeded(job.JobType);

                return JobOutcome.Succeeded;
            }

            return JobOutcome.None;
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
            // The host stopped; leave the row Running so the lease expires and another worker resumes it.
            return JobOutcome.None;
        }
        catch(OperationCanceledException exception) when(!cancellationToken.IsCancellationRequested) {
            if(Volatile.Read(ref leaseLost) is 1) {
                return JobOutcome.None;
            }

            if(Volatile.Read(ref cancellationRequested) is 1 ||
               await _store.IsCancellationRequestedAsync(job.JobId, CancellationToken.None).ConfigureAwait(false)) {
                return await CompleteCanceledAsync(job, progress.Current).ConfigureAwait(false);
            }

            return await HandleFailureAsync(job, exception, progress.Current).ConfigureAwait(false);
        }
        catch(JobLeaseLostException) {
            return JobOutcome.None;
        }
        catch(Exception exception) {
            if(Volatile.Read(ref leaseLost) is 1) {
                return JobOutcome.None;
            }

            return await HandleFailureAsync(job, exception, progress.Current).ConfigureAwait(false);
        }
        finally {
            jobCancellation.Cancel(); // NOSONAR: The synchronous API is intentional for netstandard2.0/net462 compatibility.
            await AwaitHelpersAsync(leaseTask, watchTask).ConfigureAwait(false);
        }
    }

    private async Task<JobOutcome> HandleFailureAsync(JobRecord job, Exception error, int progress) {
        if(job.Attempts < job.MaxAttempts) {
            var nextAttemptUtc = _clock.GetUtcNow() + Backoff(job.Attempts);
            var retried = await _store
                .RetryAsync(job.JobId, _owner, error, nextAttemptUtc, CancellationToken.None)
                .ConfigureAwait(false);

            if(retried) {
                _metrics.RecordRetried(job.JobType);

                return JobOutcome.Retried;
            }

            return JobOutcome.None;
        }

        return await FailPermanentAsync(job, error, progress).ConfigureAwait(false);
    }

    private async Task<JobOutcome> FailPermanentAsync(JobRecord job, Exception error, int progress) {
        var completed = await _store
            .CompleteAsync(job.JobId, _owner, JobStatus.Failed, progress, error, _clock.GetUtcNow(), CancellationToken.None)
            .ConfigureAwait(false);

        if(!completed) {
            return JobOutcome.None;
        }

        _metrics.RecordFailed(job.JobType);
        await RecordDeadLetterAsync(job, error).ConfigureAwait(false);
        _metrics.RecordDeadLettered(job.JobType);

        return JobOutcome.Failed;
    }

    private async Task<JobOutcome> CompleteCanceledAsync(JobRecord job, int progress) {
        var completed = await _store
            .CompleteAsync(job.JobId, _owner, JobStatus.Canceled, progress, null, _clock.GetUtcNow(), CancellationToken.None)
            .ConfigureAwait(false);

        if(completed) {
            _metrics.RecordCanceled(job.JobType);

            return JobOutcome.Canceled;
        }

        return JobOutcome.None;
    }

    private async Task MaintainLeaseAsync(
        string jobId,
        CancellationTokenSource cancellation,
        Action onLost,
        CancellationToken stoppingToken) {
        var heartbeat = TimeSpan.FromTicks(_options.LeaseDuration.Ticks / 2);

        if(heartbeat <= TimeSpan.Zero) {
            heartbeat = _options.LeaseDuration;
        }

        while(!cancellation.IsCancellationRequested && !stoppingToken.IsCancellationRequested) {
            try {
                await Task.Delay(heartbeat, cancellation.Token).ConfigureAwait(false);

                if(!await _store
                    .RenewLeaseAsync(jobId, _owner, _options.LeaseDuration, CancellationToken.None)
                    .ConfigureAwait(false)) {
                    onLost();
                    cancellation.Cancel(); // NOSONAR: The synchronous API is intentional for netstandard2.0/net462 compatibility.

                    return;
                }
            }
            catch(OperationCanceledException) {
                return;
            }
            catch {
                // A store outage: retry on the next heartbeat; if the lease expired, renew fails.
            }
        }
    }

    private async Task WatchCancellationAsync(
        string jobId,
        CancellationTokenSource cancellation,
        Action onRequested,
        CancellationToken stoppingToken) {
        while(!cancellation.IsCancellationRequested && !stoppingToken.IsCancellationRequested) {
            try {
                await Task.Delay(_options.CancellationPollInterval, cancellation.Token).ConfigureAwait(false);

                if(await _store.IsCancellationRequestedAsync(jobId, CancellationToken.None).ConfigureAwait(false)) {
                    onRequested();
                    cancellation.Cancel(); // NOSONAR: The synchronous API is intentional for netstandard2.0/net462 compatibility.

                    return;
                }
            }
            catch(OperationCanceledException) {
                return;
            }
            catch {
                // A store outage: retry on the next tick.
            }
        }
    }

    private async Task RecordDeadLetterAsync(JobRecord job, Exception error) {
        var info = new JobDeadLetterInfo(
            job.JobId,
            job.JobType,
            job.Attempts,
            error.GetType().FullName ?? error.GetType().Name,
            error.Message,
            job.CreatedAtUtc);
        var deadLetter = new JobDeadLetter(info, _clock.GetUtcNow(), job.TenantId);

        try {
            await _deadLetters.RecordAsync(deadLetter, error, _clock.GetUtcNow(), CancellationToken.None).ConfigureAwait(false);
        }
        catch {
            // The terminal row remains the durable record; dead-lettering is best-effort.
        }
    }

    private TimeSpan Backoff(int failedAttempt) {
        var exponent = Math.Max(0, failedAttempt - 1);
        var milliseconds = Math.Min(
            _options.InitialRetryDelay.TotalMilliseconds * Math.Pow(_options.BackoffMultiplier, exponent),
            _options.MaxRetryDelay.TotalMilliseconds);

        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static async Task AwaitHelpersAsync(Task first, Task second) {
        try {
            await Task.WhenAll(first, second).ConfigureAwait(false);
        }
        catch {
            // The helpers already contain their own cancellation and retry handling.
        }
    }

    private static JobOptions Validate(IOptions<JobOptions>? options) {
        var value = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        value.Validate();

        return value;
    }

    private enum JobOutcome {
        None = 0,
        Succeeded = 1,
        Failed = 2,
        Retried = 3,
        Canceled = 4,
    }
}
