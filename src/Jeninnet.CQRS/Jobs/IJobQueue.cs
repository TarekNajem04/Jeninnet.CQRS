//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Jobs;

/// <summary>Submits and cancels background jobs.</summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "The public contract is named IJobQueue.")]
public interface IJobQueue {
    /// <summary>Submits a typed job and returns its id.</summary>
    /// <typeparam name="TJob">The job payload type; a handler must be registered.</typeparam>
    /// <param name="job">The payload.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The job id used by <see cref="IJobClient.GetStatusAsync"/>.</returns>
    Task<string> SubmitAsync<TJob>(TJob job, CancellationToken cancellationToken = default) where TJob : class;

    /// <summary>
    /// Requests cancellation. A queued job is canceled immediately; a running job is canceled
    /// cooperatively by its worker. Terminal jobs are a no-op.
    /// </summary>
    /// <param name="jobId">The job id.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when the job exists.</returns>
    Task<bool> CancelAsync(string jobId, CancellationToken cancellationToken = default);
}

/// <summary>Polls durable job status.</summary>
public interface IJobClient {
    /// <summary>Gets the current status, or <see langword="null"/> when the id is unknown.</summary>
    /// <param name="jobId">The job id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<JobStatusResult?> GetStatusAsync(string jobId, CancellationToken cancellationToken = default);
}

/// <summary>Progress reporter bound to one leased job.</summary>
internal sealed class JobProgress(IJobStore store, string jobId, string owner, int initial) : IJobProgress {
    private readonly IJobStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly string _jobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
    private readonly string _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    private int _current = initial;

    internal int Current => Volatile.Read(ref _current);

    /// <inheritdoc />
    public async Task ReportAsync(int percent, CancellationToken cancellationToken = default) {
        var clamped = percent < 0 ? 0 : percent > 100 ? 100 : percent;
        var written = await _store.ReportProgressAsync(_jobId, _owner, clamped, cancellationToken).ConfigureAwait(false);

        if(!written) {
            throw new JobLeaseLostException(_jobId);
        }

        Volatile.Write(ref _current, clamped);
    }
}