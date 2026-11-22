//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Recurring.Extensions;

/// <summary>
/// Extension methods for registering cron-style recurring delivery.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds recurring delivery on top of durable deferred delivery: the dispatcher, overlap gate,
    /// and next-run scheduler. Requires a keyed deferred store (the built-in in-memory and EF Core
    /// stores implement it).
    /// </summary>
    /// <remarks>
    /// Each instance scheduling the same unique key converges on one pending occurrence because
    /// the occurrence key is deterministic and staging is insert-if-absent. This is not a
    /// distributed leader election: overlap protection is process-local, while durable entries
    /// are shared through the store.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsRecurringScheduling(this IServiceCollection services) {
        Guard.NotNull(services);

        services.AddCqrsDeferredDelivery();
        services.TryAddSingleton<RecurringExecutionGate>();
        services.TryAddSingleton<IRecurringScheduler, RecurringScheduler>();
        services.TryAddSingleton<IRecurringDispatcher, RecurringDispatcher>();
        services.TryAddSingleton<IKeyedDeferredStore>(static provider => {
            var store = provider.GetRequiredService<IDeferredStore>();

            return store as IKeyedDeferredStore
                ?? throw new InvalidOperationException($"The registered {nameof(IDeferredStore)} must also implement {nameof(IKeyedDeferredStore)} to support recurring schedules.");
        });

        return services;
    }
}