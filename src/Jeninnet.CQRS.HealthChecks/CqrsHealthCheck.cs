//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.HealthChecks;

/// <summary>
/// Health check reporting CQRS dispatcher availability and circuit-breaker state.
/// <list type="bullet">
/// <item><see cref="HealthStatus.Unhealthy"/> when no <see cref="ICqrsBroker"/> is registered.</item>
/// <item><see cref="HealthStatus.Degraded"/> while any registered circuit breaker is open (never unhealthy: the system still serves requests that do not touch the open breaker).</item>
/// <item><see cref="HealthStatus.Healthy"/> otherwise.</item>
/// </list>
/// </summary>
/// <remarks>
/// Initializes a new instance with the given service provider.
/// </remarks>
/// <param name="provider">The provider used to resolve the broker and breaker snapshots.</param>
public sealed class CqrsHealthCheck(IServiceProvider provider) : IHealthCheck {
    private readonly IServiceProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) {
        if(_provider.GetService<ICqrsBroker>() is null) {
            return Task.FromResult(HealthCheckResult.Unhealthy("No ICqrsBroker is registered. Call AddCqrs* registration first."));
        }

        var registry = _provider.GetService<CircuitBreakerRegistry>();

        if(registry is not null) {
            var open = registry.SnapshotAll()
                               .Where(static entry => entry.Value.State is CircuitBreakerState.Open)
                               .Select(static entry => entry.Key)
                               .ToList();

            if(open.Count > 0) {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Circuit breaker open for: {string.Join(", ", open)}.",
                    data: new Dictionary<string, object> {
                        ["openBreakers"] = open,
                        ["openBreakerCount"] = open.Count
                    }));
            }
        }

        return Task.FromResult(HealthCheckResult.Healthy("CQRS dispatchers are ready."));
    }
}