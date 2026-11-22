//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Resilience;

/// <summary>
/// Options controlling the rate-limit behavior: a fixed-window limiter partitioned by key.
/// </summary>
public sealed class RateLimitOptions {
    /// <summary>
    /// The default configuration section name for binding.
    /// </summary>
    public static string SectionName { get; internal set; } = "RateLimitOptions";

    /// <summary>
    /// Gets or sets the number of permits per window, per key. Defaults to 100.
    /// </summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>
    /// Gets or sets the fixed window length. Defaults to one second.
    /// </summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets how many acquisitions may wait for a permit. Defaults to 0 (fail fast).
    /// </summary>
    public int QueueLimit { get; set; }

    /// <summary>
    /// Gets or sets the partition key selector. Defaults to <see langword="null"/>, which partitions
    /// by request type with no allocation. A custom selector boxes value-type requests.
    /// </summary>
    public Func<object, string>? KeySelector { get; set; }
}
