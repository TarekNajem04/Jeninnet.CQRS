//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.HealthChecks;

/// <summary>
/// Reports the rider host lifecycle. It is healthy when every configured rider is started,
/// degraded while a rider is starting/stopping/not started, and unhealthy when a rider faulted.
/// The result data carries one <c>rider.{name}</c> entry per rider so an operator can see which
/// broker is responsible.
/// </summary>
/// <param name="provider">The service provider used to resolve the optional rider registry.</param>
public sealed class CqrsRiderHealthCheck(IServiceProvider provider) : IHealthCheck {
    private readonly IServiceProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) {
        var registry = _provider.GetService<CqrsRiderRegistry>();

        if(registry is null) {
            return Task.FromResult(HealthCheckResult.Healthy("No riders are configured."));
        }

        var snapshots = registry.Snapshot();
        var data = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach(var snapshot in snapshots) {
            data[$"rider.{snapshot.Name}"] = snapshot.Status.ToString();
        }

        var faulted = snapshots.Where(static snapshot => snapshot.Status is CqrsRiderStatus.Faulted).Select(static snapshot => snapshot.Name).ToArray();

        if(faulted.Length > 0) {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Rider(s) faulted: {string.Join(", ", faulted)}.",
                data: data));
        }

        var notRunning = snapshots
            .Where(static snapshot => snapshot.Status is not CqrsRiderStatus.Started)
            .Select(static snapshot => $"{snapshot.Name}={snapshot.Status}")
            .ToArray();

        if(notRunning.Length > 0) {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Rider(s) not running: {string.Join(", ", notRunning)}.",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            snapshots.Count is 0 ? "No riders are configured." : $"All {snapshots.Count} rider(s) are running.",
            data));
    }
}
