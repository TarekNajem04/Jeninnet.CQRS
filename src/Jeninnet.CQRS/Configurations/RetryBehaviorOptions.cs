//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Configurations;

/// <summary>
/// Configuration for retry logic.
/// </summary>
public record RetryBehaviorOptions(
    int MaxRetryCount = 3,
    TimeSpan BackoffInterval = default) {
    /// <summary>Gets the default configuration section name.</summary>
    public static readonly string SectionName = "RetryBehavior";
}
