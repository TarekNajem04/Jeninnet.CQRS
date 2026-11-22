//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Jobs;

/// <summary>Metric names and tags for job consumption.</summary>
public static class JobDiagnosticNames {
    /// <summary>The meter shared with the rest of the CQRS diagnostics.</summary>
    public const string MeterName = "Jeninnet.CQRS";

    /// <summary>Observable gauge for queued and running jobs.</summary>
    public const string Active = "cqrs.job.active";

    /// <summary>Counter for accepted submissions.</summary>
    public const string Submitted = "cqrs.job.submitted";

    /// <summary>Counter for handler attempts that started.</summary>
    public const string Started = "cqrs.job.started";

    /// <summary>Counter for successful terminal jobs.</summary>
    public const string Succeeded = "cqrs.job.succeeded";

    /// <summary>Counter for terminal failures.</summary>
    public const string Failed = "cqrs.job.failed";

    /// <summary>Counter for canceled terminal jobs.</summary>
    public const string Canceled = "cqrs.job.canceled";

    /// <summary>Counter for failed attempts that were requeued.</summary>
    public const string Retried = "cqrs.job.retried";

    /// <summary>Counter for jobs that exhausted their attempts and were dead-lettered.</summary>
    public const string DeadLettered = "cqrs.job.dead_lettered";

    /// <summary>Tag carrying the job payload type name.</summary>
    public const string JobTypeTag = "cqrs.job.type";

    /// <summary>Tag carrying the job status.</summary>
    public const string StatusTag = "cqrs.job.status";
}

/// <summary>
/// Owns the job instruments. The active gauge counts queued and running rows at scrape time;
/// terminal counters advance once per accepted transition.
/// </summary>
public sealed class JobMetrics : IDisposable {
    private readonly IJobStore _store;
    private readonly Meter _meter;
    private readonly Counter<long> _submitted;
    private readonly Counter<long> _started;
    private readonly Counter<long> _succeeded;
    private readonly Counter<long> _failed;
    private readonly Counter<long> _canceled;
    private readonly Counter<long> _retried;
    private readonly Counter<long> _deadLettered;
    private const string JobUnit = "{jobs}";

    /// <summary>Initializes the instruments.</summary>
    /// <param name="store">The job store used by the active gauge.</param>
    public JobMetrics(IJobStore store) {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _meter = new Meter(JobDiagnosticNames.MeterName, "1.0.0");
        _submitted = _meter.CreateCounter<long>(JobDiagnosticNames.Submitted, unit: JobUnit, description: "Jobs accepted by the queue.");
        _started = _meter.CreateCounter<long>(JobDiagnosticNames.Started, unit: "{attempts}", description: "Job handler attempts that started.");
        _succeeded = _meter.CreateCounter<long>(JobDiagnosticNames.Succeeded, unit: JobUnit, description: "Jobs that completed successfully.");
        _failed = _meter.CreateCounter<long>(JobDiagnosticNames.Failed, unit: JobUnit, description: "Jobs that exhausted their attempts.");
        _canceled = _meter.CreateCounter<long>(JobDiagnosticNames.Canceled, unit: JobUnit, description: "Jobs that ended canceled.");
        _retried = _meter.CreateCounter<long>(JobDiagnosticNames.Retried, unit: "{attempts}", description: "Failed attempts returned to the queue.");
        _deadLettered = _meter.CreateCounter<long>(JobDiagnosticNames.DeadLettered, unit: JobUnit, description: "Jobs written to the dead-letter store.");
        _meter.CreateObservableGauge(
            JobDiagnosticNames.Active,
            ObserveActive,
            unit: JobUnit,
            description: "Jobs currently queued or running.");
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();

    /// <summary>Records an accepted submission.</summary>
    /// <param name="jobType">The job payload type name.</param>
    public void RecordSubmitted(string jobType) => _submitted.Add(1, Job(jobType));

    /// <summary>Records a started handler attempt.</summary>
    /// <param name="jobType">The job payload type name.</param>
    public void RecordStarted(string jobType) => _started.Add(1, Job(jobType));

    /// <summary>Records a successful job.</summary>
    /// <param name="jobType">The job payload type name.</param>
    public void RecordSucceeded(string jobType) => _succeeded.Add(1, Job(jobType));

    /// <summary>Records a terminal failure.</summary>
    /// <param name="jobType">The job payload type name.</param>
    public void RecordFailed(string jobType) => _failed.Add(1, Job(jobType));

    /// <summary>Records a canceled job.</summary>
    /// <param name="jobType">The job payload type name.</param>
    public void RecordCanceled(string jobType) => _canceled.Add(1, Job(jobType));

    /// <summary>Records a failed attempt returning to the queue.</summary>
    /// <param name="jobType">The job payload type name.</param>
    public void RecordRetried(string jobType) => _retried.Add(1, Job(jobType));

    /// <summary>Records a job written to the dead-letter store.</summary>
    /// <param name="jobType">The job payload type name.</param>
    public void RecordDeadLettered(string jobType) => _deadLettered.Add(1, Job(jobType));

    private static KeyValuePair<string, object?> Job(string jobType) =>
        new(JobDiagnosticNames.JobTypeTag, jobType);

    private IEnumerable<Measurement<long>> ObserveActive() {
        foreach(var status in new[] { JobStatus.Queued, JobStatus.Running }) {
            long count;

            try {
                count = _store.CountAsync(status).GetAwaiter().GetResult();
            }
            catch(Exception) {
                continue;
            }

            yield return new Measurement<long>(
                count,
                new KeyValuePair<string, object?>(JobDiagnosticNames.StatusTag, status.ToString()));
        }
    }
}

/// <summary>Creates the job instruments at startup so the gauges exist before first scrape.</summary>
internal sealed class JobMetricsHostedService(JobMetrics metrics) : IHostedService {
    private readonly JobMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) {
        GC.KeepAlive(_metrics);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
