//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.HealthChecks;

/// <summary>
/// Reports routing slips that have not changed beyond <see cref="RoutingSlipHealthOptions.StuckAfter"/>.
/// Terminal slips (<see cref="RoutingSlipStatus.Completed"/>, <see cref="RoutingSlipStatus.Compensated"/>,
/// and <see cref="RoutingSlipStatus.Faulted"/>) are ignored, so retained rows do not make the host
/// unhealthy forever.
/// </summary>
public sealed class RoutingSlipStuckHealthCheck : IHealthCheck {
    private readonly IRoutingSlipStore _store;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _stuckAfter;

    /// <summary>Initializes the check.</summary>
    /// <param name="store">The routing-slip store.</param>
    /// <param name="options">The stuck threshold.</param>
    /// <param name="clock">The clock used for age.</param>
    public RoutingSlipStuckHealthCheck(
        IRoutingSlipStore store,
        IOptions<RoutingSlipHealthOptions> options,
        TimeProvider clock) {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _stuckAfter = (options ?? throw new ArgumentNullException(nameof(options))).Value.StuckAfter;

        if(_stuckAfter < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options), _stuckAfter, "StuckAfter must not be negative.");
        }
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) {
        var now = _clock.GetUtcNow();
        RoutingSlipSnapshot? oldest = null;
        var active = 0;
        var stuck = 0;

        try {
            var rows = await _store.ListAsync(definitionName: null, cancellationToken).ConfigureAwait(false);

            foreach(var row in rows) {
                if(row.Status is not (RoutingSlipStatus.Running or RoutingSlipStatus.Compensating)) {
                    continue;
                }

                active++;

                if(now - row.UpdatedAtUtc >= _stuckAfter) {
                    stuck++;
                }

                if(oldest is null || row.UpdatedAtUtc < oldest.UpdatedAtUtc) {
                    oldest = row;
                }
            }
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch(Exception exception) {
            return HealthCheckResult.Unhealthy("Failed to read the routing-slip store.", exception);
        }

        var data = new Dictionary<string, object> {
            ["active"] = active,
            ["stuck"] = 0,
            ["thresholdSeconds"] = _stuckAfter.TotalSeconds,
        };

        if(oldest is null) {
            return HealthCheckResult.Healthy("No active routing slips.", data);
        }

        var lag = now - oldest.UpdatedAtUtc;
        lag = lag < TimeSpan.Zero ? TimeSpan.Zero : lag;
        data["oldestSlipId"] = oldest.SlipId;
        data["oldestDefinition"] = oldest.DefinitionName;
        data["lagSeconds"] = lag.TotalSeconds;

        if(lag >= _stuckAfter) {
            data["stuck"] = stuck;

            return HealthCheckResult.Unhealthy(
                $"{stuck} routing slip(s) did not change for {lag.TotalSeconds:F0}s (threshold {_stuckAfter.TotalSeconds:F0}s).",
                data: data);
        }

        return HealthCheckResult.Healthy($"Oldest active routing slip changed {lag.TotalSeconds:F0}s ago.", data);
    }
}
