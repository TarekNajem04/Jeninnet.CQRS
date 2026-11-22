//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Jobs;

/// <summary>Default job submission client: serializes the payload and creates a queued row.</summary>
internal sealed class JobQueue(
    IJobStore store,
    JobHandlerRegistry handlers,
    IMessageSerializer serializer,
    IOptions<JobOptions> options,
    JobMetrics metrics,
    TimeProvider? clock = null,
    TenantAccessor? tenant = null) : IJobQueue {
    private readonly IJobStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly JobHandlerRegistry _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
    private readonly IMessageSerializer _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    private readonly JobOptions _options = Validate(options);
    private readonly JobMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly TenantAccessor? _tenant = tenant;

    /// <inheritdoc />
    public async Task<string> SubmitAsync<TJob>(TJob job, CancellationToken cancellationToken = default) where TJob : class {
        Guard.NotNull(job);

        var handler = _handlers.GetRequired(typeof(TJob));
        var now = _clock.GetUtcNow();
        var record = new JobRecord {
            JobId = Guid.NewGuid().ToString("N"),
            JobType = handler.JobTypeName,
            Payload = _serializer.Serialize(job, typeof(TJob)),
            TenantId = _tenant?.Current,
            Status = JobStatus.Queued,
            Progress = 0,
            Attempts = 0,
            MaxAttempts = _options.MaxAttempts,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            NextAttemptUtc = now,
        };

        var stored = await _store.CreateAsync(record, cancellationToken).ConfigureAwait(false);
        _metrics.RecordSubmitted(handler.JobTypeName);

        return stored.JobId;
    }

    /// <inheritdoc />
    public Task<bool> CancelAsync(string jobId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(jobId);

        return _store.RequestCancellationAsync(jobId, _clock.GetUtcNow(), cancellationToken);
    }

    private static JobOptions Validate(IOptions<JobOptions>? options) {
        var value = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        value.Validate();

        return value;
    }
}

/// <summary>Default status client.</summary>
internal sealed class JobClient(IJobStore store) : IJobClient {
    private readonly IJobStore _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async Task<JobStatusResult?> GetStatusAsync(string jobId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(jobId);

        var record = await _store.GetAsync(jobId, cancellationToken).ConfigureAwait(false);

        return record is null ? null : JobStatusResult.From(record);
    }
}