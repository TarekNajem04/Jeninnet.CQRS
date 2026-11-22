//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Jobs;

/// <summary>
/// Thrown when a running job loses its lease (another worker reclaimed it) or the job is no longer
/// running. The worker stops without writing a terminal state; the new owner is responsible.
/// </summary>
public sealed class JobLeaseLostException : InvalidOperationException {
    /// <summary>Initializes the exception for a lost lease.</summary>
    /// <param name="jobId">The job id.</param>
    public JobLeaseLostException(string jobId)
        : base($"The lease for job '{jobId}' was lost; the new owner is responsible for the job.") {
        JobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
    }

    /// <summary>Gets the job id.</summary>
    public string JobId { get; }
}

/// <summary>Both errors that made a job reach the dead-letter store after exhausting its attempts.</summary>
/// <param name="JobId">The job id.</param>
/// <param name="JobType">The job payload type name.</param>
/// <param name="Attempts">Attempts made.</param>
/// <param name="ErrorType">The last exception type name.</param>
/// <param name="ErrorMessage">The last exception message.</param>
/// <param name="CreatedAtUtc">When the job was submitted.</param>
public sealed record JobDeadLetterInfo(
    string JobId,
    string JobType,
    int Attempts,
    string ErrorType,
    string ErrorMessage,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Dead-letter envelope for a job that exhausted its attempts. It is inspectable and intentionally
/// not replayable through the generic pump: fix the handler, then submit the job again.
/// </summary>
public sealed class JobDeadLetter : IDeferredEnvelope {
    /// <summary>Initializes the dead-letter envelope.</summary>
    /// <param name="info">The failed job details.</param>
    /// <param name="failedAtUtc">When the terminal failure was observed.</param>
    /// <param name="tenantId">The tenant that submitted the job, when any.</param>
    public JobDeadLetter(JobDeadLetterInfo info, DateTimeOffset failedAtUtc, string? tenantId = null) {
        Info = info ?? throw new ArgumentNullException(nameof(info));
        DueUtc = failedAtUtc;
        TenantId = tenantId;
    }

    /// <summary>Gets the failed job details.</summary>
    public JobDeadLetterInfo Info { get; }

    /// <inheritdoc />
    public object Command => Info;

    /// <inheritdoc />
    public int Version => 1;

    /// <inheritdoc />
    public DateTimeOffset DueUtc { get; }

    /// <inheritdoc />
    public DateTimeOffset? ExpiresAtUtc => null;

    /// <inheritdoc />
    public string? TenantId { get; }

    /// <inheritdoc />
    public IDeferredEnvelope WithTenant(string? tenantId) => new JobDeadLetter(Info, DueUtc, tenantId);

    /// <inheritdoc />
    public Task DispatchAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            $"Job '{Info.JobId}' ({Info.JobType}) exhausted its attempts. Fix the handler, then submit the job again; the generic dead-letter pump cannot replay a job.");
}