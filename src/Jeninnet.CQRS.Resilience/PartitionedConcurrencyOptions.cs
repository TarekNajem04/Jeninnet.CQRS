//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Resilience;

/// <summary>
/// Options for <see cref="PartitionedConcurrencyBehavior{TRequest, TResponse}"/>: parallelism is
/// limited per partition key, not globally.
/// </summary>
public sealed class PartitionedConcurrencyOptions {
    /// <summary>
    /// The default configuration section name for binding.
    /// </summary>
    public static string SectionName { get; internal set; } = "PartitionedConcurrencyOptions";

    /// <summary>
    /// Gets or sets how many dispatches may run concurrently for one key. Defaults to 1, which
    /// serializes work on the same aggregate.
    /// </summary>
    public int ConcurrencyLimit { get; set; } = 1;

    /// <summary>
    /// Gets or sets the soft upper bound on live partitions. When exceeded after a release,
    /// least-recently-used idle partitions are evicted; a partition with a waiting or running
    /// dispatch is never evicted, so the map can briefly stay above the bound under load.
    /// Defaults to 1024.
    /// </summary>
    public int MaxPartitions { get; set; } = 1024;

    /// <summary>
    /// Gets or sets a value indicating whether a busy partition rejects immediately with
    /// <see cref="PartitionBusyException"/> instead of waiting for a permit. Defaults to
    /// <see langword="false"/> (wait until a permit is available or the token is cancelled).
    /// </summary>
    public bool FailFastWhenBusy { get; set; }

    /// <summary>
    /// Gets or sets how long a dispatch waits for a busy partition before
    /// <see cref="PartitionBusyException"/> is thrown. Defaults to <see langword="null"/>, which
    /// waits until a permit is available or the token is cancelled.
    /// </summary>
    public TimeSpan? WaitTimeout { get; set; }

    /// <summary>
    /// Gets or sets the partition key selector. Defaults to <see langword="null"/>, which
    /// partitions by request type with no allocation. A custom selector boxes value-type requests.
    /// </summary>
    public Func<object, string>? KeySelector { get; set; }
}