//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Jobs;

/// <summary>
/// Executes one strongly typed background job. The handler receives the job payload and a progress
/// reporter; it must observe the cancellation token for cooperative cancellation.
/// </summary>
/// <typeparam name="TJob">The job payload type.</typeparam>
public interface IJobHandler<in TJob> where TJob : class {
    /// <summary>Runs the job.</summary>
    /// <param name="job">The submitted payload.</param>
    /// <param name="progress">Reports progress from 0 to 100.</param>
    /// <param name="cancellationToken">Cancels the job cooperatively.</param>
    Task ExecuteAsync(TJob job, IJobProgress progress, CancellationToken cancellationToken);
}

/// <summary>Progress reporter supplied to a running job.</summary>
public interface IJobProgress {
    /// <summary>Reports progress from 0 to 100. A lost lease throws <see cref="JobLeaseLostException"/>.</summary>
    /// <param name="percent">The progress percentage; values outside 0–100 are clamped.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task ReportAsync(int percent, CancellationToken cancellationToken = default);
}
