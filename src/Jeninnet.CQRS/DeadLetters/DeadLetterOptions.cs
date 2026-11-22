//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.DeadLetters;

/// <summary>
/// Options for the dead-letter store and its pruning service.
/// </summary>
public sealed class DeadLetterOptions {
    /// <summary>
    /// Gets or sets how long an entry is kept after its last failure. Entries older than this
    /// are removed by pruning. Defaults to 30 days.
    /// </summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Gets or sets the maximum number of entries kept. When exceeded, the oldest entries
    /// (by last failure) are evicted. Defaults to 1000.
    /// </summary>
    public int MaxEntries { get; set; } = 1000;

    /// <summary>
    /// Gets or sets how often the hosted pruning service runs. Defaults to 15 minutes;
    /// non-positive values disable the timer.
    /// </summary>
    public TimeSpan PruneInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gets or sets whether the hosted pruning service runs at all. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool EnablePruning { get; set; } = true;
}
