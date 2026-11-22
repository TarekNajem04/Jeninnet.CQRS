//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//

namespace Jeninnet.CQRS.Extensions;

/// <summary>
/// Registration for the consumer inbox: the in-memory store, the middleware that wraps every
/// inbound delivery, the duplicate metric, and the retention pruner.
/// </summary>
public static partial class ServiceCollectionExtensions {
    /// <summary>
    /// Adds consumer-inbox deduplication. The same broker message id runs the handler once; a
    /// redelivery is acknowledged without invoking it. The in-memory store is the default; the EF
    /// Core package replaces it with a durable table through <c>AddCqrsEfInbox</c>.
    /// </summary>
    /// <remarks>
    /// The middleware runs for every transport adapter that uses the shared receive pipeline. It
    /// records the id before the handler and completes the row after success; a failed handler
    /// releases the claim so the broker can retry. See <see cref="InboxOptions"/> for the
    /// retention, pending timeout, and manual-completion mitigation.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional configuration of <see cref="InboxOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsInbox(
        this IServiceCollection services,
        Action<InboxOptions>? configure = null) {
        Guard.NotNull(services);

        services.AddCqrsReceivePipeline();
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<InboxOptions>();

        if(configure is not null) {
            services.Configure(configure);
        }

        services.TryAddSingleton<InboxMetrics>();
        services.TryAddSingleton<InMemoryInboxStore>();
        services.TryAddSingleton<IInboxStore>(static provider => provider.GetRequiredService<InMemoryInboxStore>());
        services.AddReceiveMiddleware<InboxReceiveMiddleware>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, InboxPruningService>());

        return services;
    }
}