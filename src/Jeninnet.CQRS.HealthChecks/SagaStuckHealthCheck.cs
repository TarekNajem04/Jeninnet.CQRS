//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.HealthChecks;

/// <summary>
/// Reports sagas that have not changed beyond <see cref="SagaHealthOptions.StuckAfter"/>. Terminal
/// states declared with <c>Completed()</c> or <c>Failure()</c> are ignored, so a retained completed
/// row does not make the host unhealthy forever.
/// </summary>
public sealed class SagaStuckHealthCheck : IHealthCheck {
    private readonly ISagaStore _store;
    private readonly ISaga[] _machines;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _stuckAfter;

    /// <summary>Initializes the check.</summary>
    /// <param name="store">The saga store. Never <see langword="null"/>.</param>
    /// <param name="machines">Configured machines. Never <see langword="null"/>.</param>
    /// <param name="options">The stuck threshold. Never <see langword="null"/>.</param>
    /// <param name="clock">The clock used for age. Never <see langword="null"/>.</param>
    public SagaStuckHealthCheck(
        ISagaStore store,
        IEnumerable<ISaga> machines,
        IOptions<SagaHealthOptions> options,
        TimeProvider clock) {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _machines = (machines ?? throw new ArgumentNullException(nameof(machines)))
            .GroupBy(static machine => machine.GetType())
            .Select(static group => group.First())
            .ToArray();
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _stuckAfter = (options ?? throw new ArgumentNullException(nameof(options))).Value.StuckAfter;

        if(_stuckAfter < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options), _stuckAfter, "StuckAfter must not be negative.");
        }
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) { // NOSONAR: Complexity is inherent to this state machine/pipeline and is covered by tests.
        var now = _clock.GetUtcNow();
        var active = 0;
        var stuck = 0;
        DateTimeOffset? oldest = null;
        Type? oldestStateType = null;

        try {
            foreach(var machine in _machines) {
                var rows = await _store.ListAsync(machine.StateType, cancellationToken).ConfigureAwait(false);

                foreach(var row in rows) {
                    if(machine.IsTerminal(row.CurrentState)) {
                        continue;
                    }

                    active++;

                    if(oldest is null || row.UpdatedAtUtc < oldest) {
                        oldest = row.UpdatedAtUtc;
                        oldestStateType = machine.StateType;
                    }
                }
            }
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch(Exception exception) {
            return HealthCheckResult.Unhealthy("Failed to read the saga store.", exception);
        }

        var data = new Dictionary<string, object> {
            ["active"] = active,
            ["stuck"] = 0,
            ["thresholdSeconds"] = _stuckAfter.TotalSeconds,
            ["machines"] = _machines.Length,
        };

        if(oldest is null) {
            return HealthCheckResult.Healthy("No active sagas.", data);
        }

        var lag = now - oldest.Value;
        lag = lag < TimeSpan.Zero ? TimeSpan.Zero : lag;
        data["oldestUpdatedAtUtc"] = oldest.Value;
        data["oldestStateType"] = oldestStateType?.Name ?? string.Empty;
        data["lagSeconds"] = lag.TotalSeconds;

        if(lag >= _stuckAfter) {
            foreach(var machine in _machines) {
                var rows = await _store.ListAsync(machine.StateType, cancellationToken).ConfigureAwait(false);
                stuck += rows.Count(row => !machine.IsTerminal(row.CurrentState) &&
                                           now - row.UpdatedAtUtc >= _stuckAfter);
            }

            data["stuck"] = stuck;

            return HealthCheckResult.Unhealthy(
                $"{stuck} saga(s) did not change for {lag.TotalSeconds:F0}s (threshold {_stuckAfter.TotalSeconds:F0}s).",
                data: data);
        }

        return HealthCheckResult.Healthy($"Oldest active saga changed {lag.TotalSeconds:F0}s ago.", data);
    }
}