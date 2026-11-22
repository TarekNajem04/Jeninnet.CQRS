//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.HealthChecks;

/// <summary>Thresholds for the saga health contribution.</summary>
public sealed class SagaHealthOptions {
    /// <summary>
    /// Gets or sets the age at which an active saga is considered stuck. Defaults to ten minutes;
    /// terminal (completed/faulted) rows never count.
    /// </summary>
    public TimeSpan StuckAfter { get; set; } = TimeSpan.FromMinutes(10);
}
