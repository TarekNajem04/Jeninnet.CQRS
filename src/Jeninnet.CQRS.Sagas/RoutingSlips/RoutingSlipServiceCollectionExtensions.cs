//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.RoutingSlips;

/// <summary>
/// Registration for routing slips: the executor, store, metrics, and definitions.
/// </summary>
public static class RoutingSlipServiceCollectionExtensions {
    /// <summary>
    /// Adds the routing-slip executor and the in-memory store. Call this once before registering
    /// definitions; the EF Core package replaces the store with <c>AddCqrsEfRoutingSlips</c>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsRoutingSlips(this IServiceCollection services) {
        Guard.NotNull(services);

        services.AddCqrsMessageSerialization();
        services.AddCqrsDeadLetters();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IRoutingSlipStore, InMemoryRoutingSlipStore>();
        services.TryAddSingleton<RoutingSlipDefinitionRegistry>();
        services.TryAddSingleton<RoutingSlipMetrics>();
        services.TryAddSingleton<RoutingSlipExecutor>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RoutingSlipStartupHostedService>());

        return services;
    }

    /// <summary>
    /// Registers one strongly typed routing-slip definition and its activity types. Definition
    /// names and arguments types must be unique; duplicates fail at host startup.
    /// </summary>
    /// <typeparam name="TArguments">The strongly typed slip arguments.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Builds the ordered steps.</param>
    /// <param name="name">The stable definition name; defaults to the arguments type name.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddRoutingSlip<TArguments>(
        this IServiceCollection services,
        Action<RoutingSlipBuilder<TArguments>> configure,
        string? name = null) where TArguments : class {
        Guard.NotNull(services);
        Guard.NotNull(configure);

        services.AddCqrsRoutingSlips();

        var builder = new RoutingSlipBuilder<TArguments>(name ?? typeof(TArguments).Name);
        configure(builder);
        var definition = builder.Build();

        services.AddSingleton<IRoutingSlipDefinition>(definition);

        foreach(var step in definition.TypedSteps) {
            services.TryAddTransient(step.ActivityType);
        }

        return services;
    }
}

/// <summary>Resolves the definition registry and metrics once at startup so invalid configurations fail fast.</summary>
internal sealed class RoutingSlipStartupHostedService(
    RoutingSlipDefinitionRegistry definitions,
    RoutingSlipMetrics metrics) : IHostedService {
    private readonly RoutingSlipDefinitionRegistry _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly RoutingSlipMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) {
        _ = _definitions.Definitions;
        GC.KeepAlive(_metrics);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}