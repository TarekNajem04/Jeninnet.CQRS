//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.HealthChecks;

/// <summary>
/// Reports deferred-command backlog: degraded at or above
/// <see cref="DurabilityHealthOptions.DeferredDegradedBacklog"/>, unhealthy at or above
/// <see cref="DurabilityHealthOptions.DeferredUnhealthyBacklog"/>. Store failures are unhealthy
/// because durability visibility is lost.
/// </summary>
public sealed class DeferredBacklogHealthCheck : IHealthCheck {
    private readonly IDeferredStore _store;
    private readonly int _degraded;
    private readonly int _unhealthy;

    /// <summary>Initializes the check.</summary>
    /// <param name="store">The deferred store. Never <see langword="null"/>.</param>
    /// <param name="options">The backlog thresholds. Never <see langword="null"/>.</param>
    public DeferredBacklogHealthCheck(IDeferredStore store, IOptions<DurabilityHealthOptions> options) {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        var value = Validate((options ?? throw new ArgumentNullException(nameof(options))).Value);
        _degraded = value.DeferredDegradedBacklog;
        _unhealthy = value.DeferredUnhealthyBacklog;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) {
        IReadOnlyList<(string Key, IDeferredEnvelope Envelope)> pending;

        try {
            pending = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch(Exception exception) {
            return HealthCheckResult.Unhealthy("Failed to read the deferred store.", exception);
        }

        var count = pending.Count;
        var data = new Dictionary<string, object> {
            ["pending"] = count,
            ["degradedThreshold"] = _degraded,
            ["unhealthyThreshold"] = _unhealthy,
            ["store"] = _store.GetType().Name,
        };

        if(count >= _unhealthy) {
            return HealthCheckResult.Unhealthy($"Deferred backlog {count} reached the hard cap {_unhealthy}.", data: data);
        }

        if(count >= _degraded) {
            return HealthCheckResult.Degraded($"Deferred backlog {count} reached the degraded threshold {_degraded}.", data: data);
        }

        return HealthCheckResult.Healthy($"Deferred backlog is {count}.", data);
    }

    private static DurabilityHealthOptions Validate(DurabilityHealthOptions options) {
        if(options.DeferredDegradedBacklog < 0) {
            throw new ArgumentOutOfRangeException(nameof(options), options.DeferredDegradedBacklog, "DeferredDegradedBacklog must not be negative.");
        }

        if(options.DeferredUnhealthyBacklog < options.DeferredDegradedBacklog) {
            throw new ArgumentOutOfRangeException(nameof(options), options.DeferredUnhealthyBacklog, "DeferredUnhealthyBacklog must be at least DeferredDegradedBacklog.");
        }

        return options;
    }
}