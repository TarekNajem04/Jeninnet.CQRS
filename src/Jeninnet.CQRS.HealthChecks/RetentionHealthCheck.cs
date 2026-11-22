//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.HealthChecks;

/// <summary>Thresholds for the retention sweeper health contribution.</summary>
public sealed class RetentionHealthOptions {
    /// <summary>
    /// Gets or sets the maximum time since the last successful sweep before health becomes
    /// degraded. Defaults to two hours.
    /// </summary>
    public TimeSpan MaxLag { get; set; } = TimeSpan.FromHours(2);
}

/// <summary>
/// Reports retention sweeper freshness. It is healthy after a successful sweep within
/// <see cref="RetentionHealthOptions.MaxLag"/>, degraded while a sweep has never completed or a
/// store failed, and healthy with a clear message when retention is not configured.
/// </summary>
public sealed class RetentionHealthCheck : IHealthCheck {
    private readonly IServiceProvider _provider;
    private readonly TimeSpan _maxLag;
    private readonly TimeProvider _clock;

    /// <summary>Initializes the check.</summary>
    /// <param name="provider">Resolves the optional <see cref="RetentionSweeper"/>.</param>
    /// <param name="options">The freshness threshold.</param>
    /// <param name="clock">The current UTC clock.</param>
    public RetentionHealthCheck(
        IServiceProvider provider,
        IOptions<RetentionHealthOptions> options,
        TimeProvider clock) {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        var value = (options ?? throw new ArgumentNullException(nameof(options))).Value;

        if(value.MaxLag <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options), value.MaxLag, "Retention MaxLag must be greater than zero.");
        }

        _maxLag = value.MaxLag;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) {
        var sweeper = _provider.GetService<RetentionSweeper>();

        if(sweeper is null) {
            return Task.FromResult(HealthCheckResult.Healthy("Retention is not configured."));
        }

        var snapshot = sweeper.Snapshot();

        if(!snapshot.Enabled) {
            return Task.FromResult(HealthCheckResult.Healthy("Retention is disabled or has no policies."));
        }

        var data = new Dictionary<string, object> {
            ["storeCount"] = snapshot.StoreCount,
        };

        if(snapshot.LastAttemptUtc is { } lastAttempt) {
            data["lastAttemptUtc"] = lastAttempt;
        }

        if(snapshot.LastSuccessUtc is { } lastSuccess) {
            data["lastSuccessUtc"] = lastSuccess;
            data["lagSeconds"] = Math.Max(0d, (_clock.GetUtcNow() - lastSuccess).TotalSeconds);
        }

        if(snapshot.LastError is { } error) {
            data["lastError"] = error;
        }

        if(snapshot.LastError is not null) {
            return Task.FromResult(HealthCheckResult.Degraded($"The last retention sweep failed: {snapshot.LastError}", data: data));
        }

        if(snapshot.LastSuccessUtc is null) {
            return Task.FromResult(HealthCheckResult.Degraded("The retention sweeper has not completed a sweep yet.", data: data));
        }

        var lag = _clock.GetUtcNow() - snapshot.LastSuccessUtc.Value;

        if(lag > _maxLag) {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"The retention sweeper has not completed a sweep for {lag.TotalMinutes:F0} minutes (threshold {_maxLag.TotalMinutes:F0}).",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Retention sweeps are current.", data));
    }
}