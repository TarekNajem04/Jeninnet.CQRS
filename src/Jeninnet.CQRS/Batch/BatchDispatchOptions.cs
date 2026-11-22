//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Batch;

/// <summary>
/// Controls how a batch dispatch schedules its items.
/// </summary>
public sealed class BatchDispatchOptions {
    /// <summary>
    /// Gets or sets the maximum number of items processed concurrently. Defaults to 1
    /// (sequential). Query batches use <see cref="DefaultQueryDegreeOfParallelism"/> when no
    /// options are supplied.
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 1;

    /// <summary>
    /// Gets the default bounded parallelism for query batches: processor count capped at 8.
    /// </summary>
    public static int DefaultQueryDegreeOfParallelism { get; } = Math.Min(Math.Max(Environment.ProcessorCount, 1), 8);
}