//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Diagnostics.Extensions;

/// <summary>
/// Extension methods for registering the diagnostics pipeline behavior.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the OpenTelemetry diagnostics behavior and its options to the service collection.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which CQRS services will be added.</param>
    /// <param name="configure">Optional configuration of the <see cref="CqrsDiagnosticsOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsDiagnostics(this IServiceCollection services, Action<CqrsDiagnosticsOptions>? configure = null) {
        Guard.NotNull(services);

        // Always registered (even with defaults) so the IOptions infrastructure exists.
        services.Configure<CqrsDiagnosticsOptions>(o => configure?.Invoke(o));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(DiagnosticsBehavior<,>));

        return services;
    }

    /// <summary>
    /// Adds observable gauges for the registered durability stores: deferred backlog and oldest
    /// due age, outbox backlog, and dead-letter count.
    /// </summary>
    /// <remarks>
    /// Call this after registering the stores: stores absent at this call get no instruments, so
    /// exporters do not see empty or fabricated series. Each gauge reads its store through the
    /// existing <c>ListAsync</c> methods when collected; there is no background loop. Gauges carry
    /// a <c>cqrs.store</c> tag with the implementation type name.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsDurabilityObservability(this IServiceCollection services) {
        Guard.NotNull(services);

        services.TryAddSingleton(TimeProvider.System);

        var signals = new DurabilitySignals(
            Deferred: services.Any(static descriptor => descriptor.ServiceType == typeof(IDeferredStore)),
            Outbox: services.Any(static descriptor => descriptor.ServiceType == typeof(IOutboxStore)),
            DeadLetters: services.Any(static descriptor => descriptor.ServiceType == typeof(IDeadLetterStore)));

        services.AddSingleton(provider => new DurabilityMetrics(
            provider,
            provider.GetRequiredService<TimeProvider>(),
            signals));
        services.AddHostedService<DurabilityMetricsHostedService>();

        return services;
    }
}