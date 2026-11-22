//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.HealthChecks;

/// <summary>
/// Reports outbox lag: the age of the oldest pending notification. Degraded at or above
/// <see cref="DurabilityHealthOptions.OutboxDegradedLag"/>, unhealthy at or above
/// <see cref="DurabilityHealthOptions.OutboxUnhealthyLag"/>. An empty outbox is healthy.
/// </summary>
public sealed class OutboxLagHealthCheck : IHealthCheck {
    private readonly IOutboxStore _store;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _degraded;
    private readonly TimeSpan _unhealthy;

    /// <summary>Initializes the check.</summary>
    /// <param name="store">The outbox store. Never <see langword="null"/>.</param>
    /// <param name="options">The lag thresholds. Never <see langword="null"/>.</param>
    /// <param name="clock">The clock used to compute lag. Never <see langword="null"/>.</param>
    public OutboxLagHealthCheck(IOutboxStore store, IOptions<DurabilityHealthOptions> options, TimeProvider clock) {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        var value = Validate((options ?? throw new ArgumentNullException(nameof(options))).Value);
        _degraded = value.OutboxDegradedLag;
        _unhealthy = value.OutboxUnhealthyLag;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) {
        IReadOnlyList<(string Key, IDeferredEnvelope Notification)> pending;

        try {
            pending = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch(Exception exception) {
            return HealthCheckResult.Unhealthy("Failed to read the outbox store.", exception);
        }

        var data = new Dictionary<string, object> {
            ["pending"] = pending.Count,
            ["degradedThresholdSeconds"] = _degraded.TotalSeconds,
            ["unhealthyThresholdSeconds"] = _unhealthy.TotalSeconds,
            ["store"] = _store.GetType().Name,
        };

        if(pending.Count is 0) {
            return HealthCheckResult.Healthy("Outbox is empty.", data);
        }

        var oldestDueUtc = pending.Min(static item => item.Notification.DueUtc);
        var lag = _clock.GetUtcNow() - oldestDueUtc;
        lag = lag < TimeSpan.Zero ? TimeSpan.Zero : lag;
        data["oldestDueUtc"] = oldestDueUtc;
        data["lagSeconds"] = lag.TotalSeconds;

        if(lag >= _unhealthy) {
            return HealthCheckResult.Unhealthy($"Outbox lag {lag.TotalSeconds:F0}s reached the hard cap {_unhealthy.TotalSeconds:F0}s.", data: data);
        }

        if(lag >= _degraded) {
            return HealthCheckResult.Degraded($"Outbox lag {lag.TotalSeconds:F0}s reached the degraded threshold {_degraded.TotalSeconds:F0}s.", data: data);
        }

        return HealthCheckResult.Healthy($"Outbox lag is {lag.TotalSeconds:F0}s.", data);
    }

    private static DurabilityHealthOptions Validate(DurabilityHealthOptions options) {
        if(options.OutboxDegradedLag < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options), options.OutboxDegradedLag, "OutboxDegradedLag must not be negative.");
        }

        if(options.OutboxUnhealthyLag < options.OutboxDegradedLag) {
            throw new ArgumentOutOfRangeException(nameof(options), options.OutboxUnhealthyLag, "OutboxUnhealthyLag must be at least OutboxDegradedLag.");
        }

        return options;
    }
}
