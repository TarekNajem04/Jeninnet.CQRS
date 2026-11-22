//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore.Extensions;

/// <summary>
/// Extension methods for registering the durable EF Core idempotency store
/// and the durable deferred-delivery store.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the EF Core idempotency store, replacing the in-memory default, and wires
    /// <see cref="IdempotencyBehavior{TRequest, TResponse}"/> into the pipeline.
    /// </summary>
    /// <remarks>
    /// The store is a singleton sharing one <see cref="IDbContextFactory{TContext}"/>:
    /// every operation opens a short-lived context, so concurrent dispatches never share
    /// tracked state. Create the schema with <c>EnsureCreatedAsync</c> or the SQL script
    /// in the package README.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configureDb">Registers the <see cref="IDbContextFactory{IdempotencyDbContext}"/> when given.</param>
    /// <param name="configureStore">Configures <see cref="EfIdempotencyStoreOptions"/> when given.</param>
    /// <param name="configureBehavior">Configures <see cref="IdempotencyBehaviorOptions"/> when given.</param>
    public static IServiceCollection AddCqrsEfIdempotencyStore(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDb = null,
        Action<EfIdempotencyStoreOptions>? configureStore = null,
        Action<IdempotencyBehaviorOptions>? configureBehavior = null) {
        ArgumentNullException.ThrowIfNull(services);

        AddMessagingSupport(services);

        if(configureDb is not null) {
            services.AddDbContextFactory<IdempotencyDbContext>(configureDb);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<EfIdempotencyStoreOptions>();
        services.AddOptions<IdempotencyBehaviorOptions>();

        if(configureStore is not null) {
            services.Configure(configureStore);
        }

        if(configureBehavior is not null) {
            services.Configure(configureBehavior);
        }

        services.TryAddSingleton<IIdempotencyStore, EfIdempotencyStore>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(IdempotencyBehavior<,>));

        return services;
    }

    /// <summary>
    /// Adds durable deferred delivery: the in-process pump plus an EF Core durability
    /// log, so scheduled commands survive restarts and are replayed at startup.
    /// </summary>
    /// <remarks>
    /// Registers the core pump (see <c>AddCqrsDeferredDelivery</c>) and replaces the
    /// in-memory durability log with <see cref="EfDeferredStore"/> (last registration
    /// wins over the default). Recovery runs at startup when enabled in
    /// <see cref="DeferredDeliveryOptions"/>.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configureDb">Registers the <see cref="IDbContextFactory{IdempotencyDbContext}"/> when given.</param>
    /// <param name="configureStore">Configures <see cref="EfDeferredStoreOptions"/> when given.</param>
    public static IServiceCollection AddCqrsEfDeferredDelivery(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDb = null,
        Action<EfDeferredStoreOptions>? configureStore = null) {
        ArgumentNullException.ThrowIfNull(services);

        AddMessagingSupport(services);
        services.AddCqrsDeferredDelivery();
        services.AddCqrsEfDeadLetters();

        if(configureDb is not null) {
            services.AddDbContextFactory<IdempotencyDbContext>(configureDb);
        }

        services.AddOptions<EfDeferredStoreOptions>();

        if(configureStore is not null) {
            services.Configure(configureStore);
        }

        services.AddSingleton<IDeferredStore, EfDeferredStore>();

        return services;
    }

    /// <summary>
    /// Adds the durable EF Core dead-letter store, replacing the in-memory default. Failures
    /// recorded by the deferred pump (and by any other caller) survive restarts and can be
    /// replayed through <see cref="IDeferredDispatcher.ReplayAsync"/>.
    /// </summary>
    /// <remarks>
    /// The store lives beside <see cref="EfDeferredStore"/> in the same database. Create the
    /// schema with <c>EnsureCreatedAsync</c> or the SQL script in the package README.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configureDb">Registers the <see cref="IDbContextFactory{IdempotencyDbContext}"/> when given.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsEfDeadLetters(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDb = null) {
        ArgumentNullException.ThrowIfNull(services);

        AddMessagingSupport(services);
        services.AddCqrsDeadLetters();

        if(configureDb is not null) {
            services.AddDbContextFactory<IdempotencyDbContext>(configureDb);
        }

        services.AddSingleton<IDeadLetterStore, EfDeadLetterStore>();

        return services;
    }

    /// <summary>
    /// Adds the durable EF Core notification outbox, replacing the in-memory store. Staged
    /// notifications survive restarts and are delivered by <see cref="OutboxDispatcher"/>.
    /// </summary>
    /// <remarks>
    /// Registers the outbox decorator and dispatcher (see <c>AddCqrsNotificationOutbox</c>) and
    /// replaces the store with <see cref="EfOutboxStore"/> (last registration wins). The outbox
    /// shares the package database and schema story; create it with <c>EnsureCreatedAsync</c> or
    /// the SQL script in the package README.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configureDb">Registers the <see cref="IDbContextFactory{IdempotencyDbContext}"/> when given.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsEfNotificationOutbox(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDb = null) {
        ArgumentNullException.ThrowIfNull(services);

        AddMessagingSupport(services);
        services.AddCqrsNotificationOutbox();

        if(configureDb is not null) {
            services.AddDbContextFactory<IdempotencyDbContext>(configureDb);
        }

        services.AddOptions<EfRetentionOptions>();
        services.AddSingleton<IOutboxStore, EfOutboxStore>();

        return services;
    }

    /// <summary>
    /// Adds the durable EF Core claim-check store, replacing the in-memory default. Payloads
    /// above <see cref="ClaimCheckOptions.ThresholdBytes"/> move here and rows keep references.
    /// </summary>
    /// <remarks>
    /// Registers the message serializer seam and the threshold policy too, so the EF stores can
    /// be used without calling the core extensions separately. The claim-check table shares the
    /// package database and schema story; see the README for the SQL and migration notes.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configureDb">Registers the <see cref="IDbContextFactory{IdempotencyDbContext}"/> when given.</param>
    /// <param name="configure">Optional configuration of <see cref="ClaimCheckOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsEfClaimCheck(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDb = null,
        Action<ClaimCheckOptions>? configure = null) {
        ArgumentNullException.ThrowIfNull(services);

        AddMessagingSupport(services);
        services.AddCqrsClaimCheck(configure);

        if(configureDb is not null) {
            services.AddDbContextFactory<IdempotencyDbContext>(configureDb);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IClaimCheckStore, EfClaimCheckStore>();

        return services;
    }

    /// <summary>
    /// Adds durable request/response pending rows by replacing the in-memory inbox with
    /// <see cref="EfRequestInbox"/>. The client still awaits responses in-process; the row gives
    /// the inbox a TTL and survives a client crash until pruning.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configureDb">Registers the <see cref="IdempotencyDbContext"/> factory when given.</param>
    /// <param name="configure">Optional configuration of <see cref="RequestResponseOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsEfRequestResponse(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDb = null,
        Action<RequestResponseOptions>? configure = null) {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCqrsRequestResponse(configure);

        if(configureDb is not null) {
            services.AddDbContextFactory<IdempotencyDbContext>(configureDb);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IRequestInbox, EfRequestInbox>();

        return services;
    }

    /// <summary>
    /// Adds the persistent scheduler with its EF Core store, replacing the in-memory default so
    /// delayed/one-shot/recurring schedules survive restarts.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configureDb">Registers the <see cref="IdempotencyDbContext"/> factory when given.</param>
    /// <param name="configure">Optional configuration of <see cref="SchedulerOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsEfScheduler(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDb = null,
        Action<SchedulerOptions>? configure = null) {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCqrsScheduler(configure);

        if(configureDb is not null) {
            services.AddDbContextFactory<IdempotencyDbContext>(configureDb);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ISchedulerStore, EfSchedulerStore>();

        return services;
    }

    /// <summary>
    /// Adds durable consumer-inbox deduplication by replacing the in-memory store with
    /// <see cref="EfInboxStore"/>. Processed message ids survive restarts, so a broker
    /// redelivery after a crash cannot repeat the handler.
    /// </summary>
    /// <remarks>
    /// The row lives beside the other durability tables in <see cref="IdempotencyDbContext"/>;
    /// create the schema with <c>EnsureCreatedAsync</c> or the SQL script in the package README.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configureDb">Registers the <see cref="IdempotencyDbContext"/> factory when given.</param>
    /// <param name="configure">Optional configuration of <see cref="InboxOptions"/>.</param>
    /// <param name="configureStore">Optional configuration of <see cref="EfInboxStoreOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsEfInbox(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDb = null,
        Action<InboxOptions>? configure = null,
        Action<EfInboxStoreOptions>? configureStore = null) {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCqrsInbox(configure);

        if(configureDb is not null) {
            services.AddDbContextFactory<IdempotencyDbContext>(configureDb);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<EfInboxStoreOptions>();

        if(configureStore is not null) {
            services.Configure(configureStore);
        }

        services.AddSingleton<IInboxStore, EfInboxStore>();

        return services;
    }

    /// <summary>
    /// Replaces the in-memory job store with <see cref="EfJobStore"/>. Register handlers with
    /// <c>AddJobHandler&lt;TJob, THandler&gt;()</c>; this call may come before or after them because
    /// the durable store is the last registration.
    /// </summary>
    /// <remarks>
    /// The rows live in <see cref="IdempotencyDbContext"/> beside the other durability tables.
    /// Create the schema with <c>EnsureCreatedAsync</c>. Without <paramref name="configureDb"/>,
    /// register <see cref="IDbContextFactory{IdempotencyDbContext}"/> yourself first. Pair it with
    /// <c>AddCqrsEfDeadLetters</c> when terminal job failures must survive a restart.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configureDb">Registers the <see cref="IdempotencyDbContext"/> factory when given.</param>
    /// <param name="configure">Optional configuration of <see cref="JobOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsEfJobs(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDb = null,
        Action<JobOptions>? configure = null) {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCqrsJobs(configure);

        if(configureDb is not null) {
            services.AddDbContextFactory<IdempotencyDbContext>(configureDb);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IJobStore, EfJobStore>();

        return services;
    }

    /// <summary>
    /// Adds the durable retention adapters for completed inbox, dead-letter, terminal job, and
    /// completed outbox rows, plus the core hosted sweeper.
    /// </summary>
    /// <remarks>
    /// Policies are explicit and dry-run is the core default. Enable outbox completion archiving
    /// by registering this extension; without it completed outbox rows keep their historical
    /// delete-on-completion behavior. Create the archive tables with <c>EnsureCreatedAsync</c> or
    /// the package README schema.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configureDb">Registers the <see cref="IdempotencyDbContext"/> factory when given.</param>
    /// <param name="configure">Configures the interval, batch, dry-run flag, and per-store policies.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsEfRetention(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDb = null,
        Action<RetentionOptions>? configure = null) {
        ArgumentNullException.ThrowIfNull(services);

        AddMessagingSupport(services);
        services.AddCqrsRetention(configure);

        if(configureDb is not null) {
            services.AddDbContextFactory<IdempotencyDbContext>(configureDb);
        }

        services.AddOptions<EfRetentionOptions>();
        services.Configure<EfRetentionOptions>(static options => options.ArchiveCompletedOutbox = true);

        services.AddSingleton<IRetentionStore, EfInboxRetentionStore>();
        services.AddSingleton<IRetentionStore, EfDeadLetterRetentionStore>();
        services.AddSingleton<IRetentionStore, EfJobRetentionStore>();
        services.AddSingleton<IRetentionStore>(static provider => new EfOutboxRetentionStore(
            provider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>(),
            provider.GetService<ClaimCheckPolicy>()));

        return services;
    }

    private static void AddMessagingSupport(IServiceCollection services) {
        services.AddCqrsMessageSerialization();
        services.AddCqrsMessageUpgrading();
        services.AddOptions<ClaimCheckOptions>();
        services.TryAddSingleton<ClaimCheckPolicy>();
    }
}
