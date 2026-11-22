//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.HealthChecks;

/// <summary>
/// Reports the scheduler due backlog. Degraded at or above <see cref="SchedulerHealthOptions.DueDegraded"/>,
/// unhealthy at or above <see cref="SchedulerHealthOptions.DueUnhealthy"/>.
/// </summary>
public sealed class SchedulerBacklogHealthCheck : IHealthCheck {
    private readonly ISchedulerStore _store;
    private readonly TimeProvider _clock;
    private readonly int _degraded;
    private readonly int _unhealthy;

    /// <summary>Initializes the check.</summary>
    /// <param name="store">The scheduler store.</param>
    /// <param name="options">The backlog thresholds.</param>
    /// <param name="clock">The clock used for the due scan.</param>
    public SchedulerBacklogHealthCheck(ISchedulerStore store, IOptions<SchedulerHealthOptions> options, TimeProvider clock) {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        var value = (options ?? throw new ArgumentNullException(nameof(options))).Value;

        if(value.DueDegraded < 0) {
            throw new ArgumentOutOfRangeException(nameof(options), value.DueDegraded, "DueDegraded must not be negative.");
        }

        if(value.DueUnhealthy < value.DueDegraded) {
            throw new ArgumentOutOfRangeException(nameof(options), value.DueUnhealthy, "DueUnhealthy must be at least DueDegraded.");
        }

        _degraded = value.DueDegraded;
        _unhealthy = value.DueUnhealthy;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) {
        long due;

        try {
            due = await _store.CountDueAsync(_clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch(Exception exception) {
            return HealthCheckResult.Unhealthy("Failed to read the scheduler store.", exception);
        }

        var data = new Dictionary<string, object> {
            ["due"] = due,
            ["degradedThreshold"] = _degraded,
            ["unhealthyThreshold"] = _unhealthy,
        };

        if(due >= _unhealthy) {
            return HealthCheckResult.Unhealthy($"Scheduler due backlog {due} reached the hard cap {_unhealthy}.", data: data);
        }

        if(due >= _degraded) {
            return HealthCheckResult.Degraded($"Scheduler due backlog {due} reached the degraded threshold {_degraded}.", data: data);
        }

        return HealthCheckResult.Healthy($"Scheduler due backlog is {due}.", data);
    }
}
