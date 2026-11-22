//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Extensions;

/// <summary>
/// Extension methods for registering the core CQRS dispatchers,
/// handlers, and optional pipeline behaviors without any external scanner.
/// </summary>
public static partial class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the necessary services for implementing the CQRS (Command Query Responsibility Segregation) pattern to the specified service collection.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the CQRS services will be added.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    /// <exception cref="ArgumentNullException"> Thrown if <paramref name="services"/> is <see langword="null"/>. </exception>
    public static IServiceCollection AddCqrsByReflection([DisallowNull] this IServiceCollection services) => AddCqrsByReflection(services, new CqrsOptions(new PipelineBehaviorsOptions()), AppDomain.CurrentDomain.GetAssemblies());

    /// <summary>
    /// Registers CQRS using reflection with the supplied options configuration.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configureOptions">Configures <see cref="CqrsOptions"/> before registration.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsByReflection([DisallowNull] this IServiceCollection services, Action<CqrsOptions> configureOptions) {
        var options = new CqrsOptions(new PipelineBehaviorsOptions());
        configureOptions(options);
        return AddCqrsByReflection(services, options);
    }

    /// <summary>
    /// Registers CQRS using reflection with the supplied options and the calling assembly.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="options">The CQRS options.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsByReflection([DisallowNull] this IServiceCollection services, CqrsOptions options)
        => AddCqrsByReflection(services, options, Assembly.GetCallingAssembly());

    /// <summary>
    /// Configures the CQRS system using source-generated registration.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the CQRS services will be added.</param>
    /// <param name="configureOptions">Action to configure the <see cref="CqrsOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsSourceGenerated([DisallowNull] this IServiceCollection services, Action<CqrsOptions> configureOptions) {
        var options = new CqrsOptions(new PipelineBehaviorsOptions());
        configureOptions(options);

        // Core Dispatchers
        AddCoreDispatchers(services);

        // Built-in Behaviors via Options
        AddPipelineBehaviors(services, options);

        services.AddCqrsNotifications(o => o.Strategy = options.NotificationStrategy);

        return services;
    }

    /// <summary>
    /// Registers CQRS by scanning the supplied assemblies for handlers and behaviors.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="options">The CQRS options.</param>
    /// <param name="assemblies">Assemblies to scan; falls back to the calling assembly.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsByReflection([DisallowNull] this IServiceCollection services, CqrsOptions options, params IEnumerable<Assembly> assemblies) {

        Guard.NotNull(services);
        Guard.NotNull(options);

        assemblies = assemblies.Where(static x => x.GetName().Name != typeof(ServiceCollectionExtensions).Assembly.GetName().Name);

        if(!assemblies.Any()) {
            assemblies = [Assembly.GetCallingAssembly()];
        }

        // 1. Register the mediator itself
        AddCoreDispatchers(services);

        // 2. Scan this assembly for handlers & behaviors
        var types = assemblies.SelectMany(static asmp => asmp.GetTypes())
                               .Where(static type => type.IsClass && !type.IsAbstract);

        // 3. Register all ICommandHandler
        RegisterOpenGeneric(services, typeof(ICommandHandler<,>), types);
        RegisterOpenGeneric(services, typeof(ICommandHandler<>), types);

        // 4. Register all IQueryHandler<TQuery, TResult>
        RegisterOpenGeneric(services, typeof(IQueryHandler<,>), types);

        // 5. Register all IPipelineBehavior<TRequest, TResponse>

        // Register built-in behaviors only if enabled in options.
        AddPipelineBehaviors(services, options);

        if(options.PipelineBehaviors.EnableValidation) {
            // ValidationBehavior is in a separate project (Jeninnet.CQRS.Validation).
            // To avoid a hard dependency in the core library, we can't refer to it by type.
        }

        // Register any custom behaviors found during reflection scan
        var customBehaviors = types.Where(
            type =>
            type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>)) &&
            !type.Name.Contains("LoggingBehavior") &&
            !type.Name.Contains("ErrorHandlingBehavior") &&
            !type.Name.Contains("MetricsBehavior") &&
            !type.Name.Contains("TransactionBehavior") &&
            !type.Name.Contains("ValidationBehavior")
        );

        foreach(var behavior in customBehaviors) {
            if(behavior.IsGenericTypeDefinition) {
                services.AddTransient(typeof(IPipelineBehavior<,>), behavior);

                continue;
            }

            // A closed generic (for example a test spy for one command) cannot be registered as
            // an open generic service; register its concrete pipeline contract instead.
            foreach(var contract in behavior.GetInterfaces().Where(static type =>
                type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>))) {
                services.AddTransient(contract, behavior);
            }
        }

        // 6. Pre-/Post-Processors
        RegisterOpenGeneric(services, typeof(IPreProcessor<>), types);
        RegisterOpenGeneric(services, typeof(IPostProcessor<,>), types);

        // 7. Notification handlers + publisher (strategy comes from CqrsOptions)
        RegisterOpenGeneric(services, typeof(INotificationHandler<>), types);
        services.AddCqrsNotifications(o => o.Strategy = options.NotificationStrategy);

        // 8. Convention handlers (Handle/HandleAsync without an interface); explicit interfaces win.
        ConventionHandlerRegistration.Register(services, types);

        return services;
    }

    /// <summary>
    /// Adds the ErrorHandling pipeline behavior to the DI container.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which behaviors will be added.</param>
    /// <returns>The original <see cref="IServiceCollection"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="services"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddCqrsErrorHandlingBehavior(this IServiceCollection services) {
        Guard.NotNull(services);

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ErrorHandlingBehavior<,>));

        return services;
    }

    /// <summary>
    /// Adds the Metrics pipeline behavior to the DI container.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which behaviors will be added.</param>
    /// <returns>The original <see cref="IServiceCollection"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="services"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddCqrsMetricsBehavior(this IServiceCollection services) {
        Guard.NotNull(services);

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(MetricsBehavior<,>));

        return services;
    }

    /// <summary>
    /// Adds the notification publisher and its dependencies to the service collection.
    /// </summary>
    /// <remarks>
    /// The publisher is registered as a singleton: it is stateless and resolves handlers
    /// from the provider on every publish call. Handler registrations still control the
    /// handlers' own lifetimes.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to which notification services will be added.</param>
    /// <param name="configure">Optional configuration of the <see cref="Notifications.NotificationPublishOptions"/>.</param>
    /// <returns>The original <see cref="IServiceCollection"/> instance.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="services"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddCqrsNotifications(this IServiceCollection services, Action<Notifications.NotificationPublishOptions>? configure = null) {
        Guard.NotNull(services);

        // Always registered (even with defaults) so the IOptions infrastructure exists.
        services.Configure<Notifications.NotificationPublishOptions>(o => configure?.Invoke(o));
        services.TryAddSingleton<Notifications.INotificationPublisher, Notifications.DefaultNotificationPublisher>();

        // The immediate publisher is also available under a scoped contract: outbox replay and
        // scoped dispatch need handlers resolved from the current scope, and replay must bypass
        // the outbox decorator entirely.
        services.TryAddScoped<Notifications.IDirectNotificationPublisher, Notifications.DefaultNotificationPublisher>();

        return services;
    }

    /// <summary>
    /// Adds in-process deferred command delivery (pump + <see cref="IDeferredDispatcher"/>).
    /// </summary>
    /// <remarks>
    /// The dispatcher is registered as a singleton shared with the hosted pump: deferrals are
    /// cross-dispatch state and must survive the call that scheduled them. Every deferral is
    /// staged in the <see cref="IDeferredStore"/> (in-memory by default; replace it with the
    /// EF Core store for restart safety) and replayed at startup unless disabled in
    /// <see cref="DeferredDeliveryOptions"/>.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to which CQRS services will be added.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="services"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddCqrsDeferredDelivery(this IServiceCollection services) {
        Guard.NotNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<DeferredDeliveryOptions>();
        services.TryAddSingleton<IDeferredStore, InMemoryDeferredStore>();
        services.TryAddSingleton<IMessageRelay, InProcessMessageRelay>();
        services.AddCqrsDeadLetters();
        services.TryAddSingleton<IDeferredDispatcher, DeferredDispatcher>();

        // The same instance serves both contracts: resolve the dispatcher, reuse it as the hosted pump.
        // (TryAddEnumerable cannot express "same instance, two contracts", so this is a plain conditional add.)
        if(!services.Any(static descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(DeferredDispatcher))) {
            services.AddSingleton<IHostedService, DeferredDispatcher>(static provider => (DeferredDispatcher)provider.GetRequiredService<IDeferredDispatcher>());
        }

        return services;
    }

    /// <summary>
    /// Adds the dead-letter store (in-memory by default) and its hosted pruning service.
    /// </summary>
    /// <remarks>
    /// Deferred delivery calls this automatically; call it directly when another component
    /// should record failures too. Replace the store with the EF Core implementation for
    /// restart safety. Replay goes through <see cref="IDeferredDispatcher.ReplayAsync"/>,
    /// because re-staging must wake the pump that owns the in-memory queue.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional configuration of <see cref="DeadLetterOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsDeadLetters(this IServiceCollection services, Action<DeadLetterOptions>? configure = null) {
        Guard.NotNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<DeadLetterOptions>();

        if(configure is not null) {
            services.Configure(configure);
        }

        services.TryAddSingleton<IDeadLetterStore, InMemoryDeadLetterStore>();

        if(!services.Any(static descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(DeadLetterPruningService))) {
            services.AddSingleton<IHostedService, DeadLetterPruningService>();
        }

        return services;
    }

    /// <summary>
    /// Adds the default JSON message serializer used by the durable EF Core stores.
    /// </summary>
    /// <remarks>
    /// <see cref="JsonMessageSerializer"/> keeps the historical web-defaults JSON payloads.
    /// Register a custom <see cref="IMessageSerializer"/> before or after this call to replace
    /// it: first registration wins (<c>TryAdd</c>), and a user registration is still last.
    /// Optional format packages (<c>Jeninnet.CQRS.Serialization.NewtonsoftJson</c>,
    /// <c>Jeninnet.CQRS.Serialization.MessagePack</c>) add <see cref="MessageSerializerFormat"/>
    /// registrations without replacing the default; receive-side code negotiates through
    /// <see cref="IMessageSerializerRegistry"/> or <see cref="TransportMessageReader"/>.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional configuration of <see cref="JsonMessageSerializerOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsMessageSerialization(this IServiceCollection services, Action<JsonMessageSerializerOptions>? configure = null) {
        Guard.NotNull(services);

        services.AddOptions<JsonMessageSerializerOptions>();

        if(configure is not null) {
            services.Configure(configure);
        }

        services.TryAddSingleton<IMessageSerializer, JsonMessageSerializer>();
        services.TryAddSingleton<MessageSerializerRegistry>();
        services.TryAddSingleton<IMessageSerializerRegistry>(static provider => provider.GetRequiredService<MessageSerializerRegistry>());
        services.TryAddSingleton<TransportMessageReader>();

        return services;
    }

    /// <summary>
    /// Adds the durable-message upgrade policy. Register <see cref="IMessageUpgrader"/>
    /// implementations to evolve stored payloads; without any, every message is version 1.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsMessageUpgrading(this IServiceCollection services) {
        Guard.NotNull(services);

        services.TryAddSingleton<MessageUpgradePolicy>();

        return services;
    }

    /// <summary>
    /// Adds claim-check storage (in-memory by default) and the threshold policy used by the
    /// durable stores.
    /// </summary>
    /// <remarks>
    /// Payloads larger than <see cref="ClaimCheckOptions.ThresholdBytes"/> are written to the
    /// claim-check store and the row keeps only a reference. Replace the store with the EF Core
    /// implementation for restart safety; without a store, every payload stays inline.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional configuration of <see cref="ClaimCheckOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsClaimCheck(this IServiceCollection services, Action<ClaimCheckOptions>? configure = null) {
        Guard.NotNull(services);

        services.AddOptions<ClaimCheckOptions>();

        if(configure is not null) {
            services.Configure(configure);
        }

        services.TryAddSingleton<IClaimCheckStore, InMemoryClaimCheckStore>();
        services.TryAddSingleton<ClaimCheckPolicy>();

        return services;
    }

    /// <summary>
    /// Adds durable notification staging: an <see cref="IOutboxStore"/> (in-memory by default),
    /// an <see cref="OutboxNotificationPublisher"/> decorator, and the retry dispatcher.
    /// </summary>
    /// <remarks>
    /// Inside a command wrapped by <see cref="AddCqrsTransactionBehavior"/> the decorator queues
    /// notifications; the transaction behavior's participant hook stages and publishes them only
    /// after commit, and a rollback discards them. Outside a transaction the notification is
    /// staged and published immediately. Delivery is at-least-once: a publish failure or a crash
    /// after staging leaves the entry for <see cref="OutboxDispatcher"/>, so notification handlers
    /// must be idempotent. Replace the store with the EF Core implementation for restart safety.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional configuration of <see cref="NotificationOutboxOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsNotificationOutbox(this IServiceCollection services, Action<NotificationOutboxOptions>? configure = null) {
        Guard.NotNull(services);

        services.AddCqrsNotifications();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IMessageRelay, InProcessMessageRelay>();
        services.TryAddSingleton<IOutboxStore, InMemoryOutboxStore>();
        services.AddOptions<NotificationOutboxOptions>();
        services.AddOptions<OutboxRelayOptions>();

        if(configure is not null) {
            services.Configure(configure);
        }

        // Scoped, and registered after the default publisher: the last registration wins for
        // single resolution, while IEnumerable<INotificationPublisher> still sees both.
        services.AddScoped<OutboxSession>();
        services.AddScoped<ITransactionParticipant>(static provider => provider.GetRequiredService<OutboxSession>());
        services.AddScoped<INotificationPublisher, OutboxNotificationPublisher>();
        services.AddHostedService<OutboxDispatcher>();

        return services;
    }

    /// <summary>
    /// Adds a hosted transport relay for the notification outbox. Staged notifications are
    /// published through the registered <see cref="IMessageTransport"/> with a persisted lease,
    /// exponential backoff, and a poison threshold that moves rows to
    /// <see cref="IDeadLetterStore"/>.
    /// </summary>
    /// <remarks>
    /// Call this after registering the transport. It calls <see cref="AddCqrsNotificationOutbox"/>
    /// first, then switches the outbox into relay mode: the scoped publisher only stages, the
    /// in-process <see cref="OutboxDispatcher"/> stands down, and one hosted
    /// <see cref="OutboxRelayWorker{TTransport}"/> owns the leased batch. Register durability
    /// health checks separately with <c>AddCqrsDurabilityHealthChecks()</c>.
    /// Delivery is at-least-once; notification handlers must be idempotent.
    /// </remarks>
    /// <typeparam name="TTransport">The transport implementation to resolve and relay through.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional configuration of <see cref="OutboxRelayOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsOutboxRelay<TTransport>(
        this IServiceCollection services,
        Action<OutboxRelayOptions>? configure = null)
        where TTransport : class, IMessageTransport {
        Guard.NotNull(services);

        services.AddCqrsNotificationOutbox();
        services.AddOptions<OutboxRelayOptions>();

        if(configure is not null) {
            services.Configure(configure);
        }

        // Relay mode wins last: the dispatcher and immediate publish path must stand down.
        services.Configure<OutboxRelayOptions>(static options => options.Enabled = true);
        services.TryAddSingleton<OutboxRelayMetrics>();
        services.TryAddSingleton<TTransport>(static provider => {
            var transport = provider.GetRequiredService<IMessageTransport>();

            return transport as TTransport
                ?? throw new InvalidOperationException(
                    $"The registered {nameof(IMessageTransport)} is '{transport.GetType().Name}', not '{typeof(TTransport).Name}'. Register the transport selected by AddCqrsOutboxRelay<{typeof(TTransport).Name}>() or use its concrete type.");
        });
        services.AddHostedService<OutboxRelayWorker<TTransport>>();

        return services;
    }

    /// <summary>
    /// Adds the transaction pipeline behavior to the DI container.
    /// </summary>
    /// <remarks>
    /// Registers a no-op <see cref="Behaviors.ITransactionScope"/> default so the behavior
    /// resolves without a real transaction provider. Register your own
    /// <see cref="Behaviors.ITransactionScope"/> implementation afterwards to make
    /// transactions real; the last registration wins.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to which behaviors will be added.</param>
    /// <returns>The original <see cref="IServiceCollection"/> instance.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="services"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddCqrsTransactionBehavior(this IServiceCollection services) {
        Guard.NotNull(services);

        services.TryAddTransient<Behaviors.ITransactionScope, Behaviors.NullTransactionScope>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(Behaviors.TransactionBehavior<,>));

        return services;
    }

    /// <summary>
    /// Adds opt-in startup validation of handler and behavior registrations.
    /// </summary>
    /// <remarks>
    /// Call this after registering handlers (last in the chain): the validator inspects a
    /// snapshot of the service collection taken at this call. Findings are: unresolvable
    /// dependencies, singleton→scoped captive dependencies, duplicate single-handler messages,
    /// and open generics that fail to close. A missing handler for a message is **not** a
    /// finding — a command may legitimately have no handler in a deployment.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional configuration of <see cref="HandlerValidationOptions"/>.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsHandlerValidation(this IServiceCollection services, Action<HandlerValidationOptions>? configure = null) {
        Guard.NotNull(services);

        services.AddOptions<HandlerValidationOptions>();

        if(configure is not null) {
            services.Configure(configure);
        }

        services.AddSingleton(new HandlerRegistrationSnapshot([.. services]));
        services.TryAddSingleton<IHandlerValidator, HandlerValidator>();
        services.AddSingleton<IHostedService, HandlerValidationHostedService>();

        return services;
    }

    /// <summary>Registers the broker and the command/query dispatchers exactly once.</summary>
    /// <param name="services">The service collection.</param>
    private static void AddCoreDispatchers(IServiceCollection services) {
        services.AddSingleton<ICqrsBroker, CqrsBroker>();
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
        services.AddSingleton<IQueryDispatcher, QueryDispatcher>();
    }

    /// <summary>Registers the built-in pipeline behaviors enabled in <paramref name="options"/>.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The CQRS options.</param>
    private static void AddPipelineBehaviors(IServiceCollection services, CqrsOptions options) {
        if(options.PipelineBehaviors.EnableLogging) {
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        }

        if(options.PipelineBehaviors.EnableErrorHandling) {
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ErrorHandlingBehavior<,>));
        }

        if(options.PipelineBehaviors.EnableMetrics) {
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(MetricsBehavior<,>));
        }

        if(options.PipelineBehaviors.EnableTransaction) {
            services.AddCqrsTransactionBehavior();
        }
    }

    /// <summary>
    /// Registers all implementations of a specified open generic interface with the dependency injection container.
    /// </summary>
    /// <remarks>
    /// For each type in <paramref name="types"/>, this method registers all implemented closed versions of <paramref name="openGenericInterface"/> as transient services.
    /// Only interfaces that are constructed from the specified open generic interface are registered.
    /// This is typically used to automate registration of generic handlers or services in applications using dependency injection.
    /// </remarks>
    /// <param name="services">The service collection to which the open generic interface implementations will be added.</param>
    /// <param name="openGenericInterface">The open generic interface type to register. Must be a generic type definition (for example,typeof(IMyInterface&lt;&gt;)).</param>
    /// <param name="types">A collection of types to scan for implementations of the open generic interface.</param>
    private static void RegisterOpenGeneric(IServiceCollection services, Type openGenericInterface, IEnumerable<Type> types) {
        foreach(var implType in types) {
            foreach(var iface in implType.GetInterfaces()) {
                if(!iface.IsGenericType) {
                    continue;
                }

                if(iface.GetGenericTypeDefinition() != openGenericInterface) {
                    continue;
                }

                services.AddTransient(iface, implType);
            }
        }
    }
}