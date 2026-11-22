//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Resilience.Extensions;

/// <summary>
/// Extension methods for registering the <see cref="RetryBehavior{TRequest, TResponse}"/>.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the resilience (retry behavior) behaviors to the CQRS pipeline.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the CQRS services will be added.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsResilienceBehavior([DisallowNull] this IServiceCollection services) => AddCqrsResilienceBehavior(services, new RetryBehaviorOptions());
    /// <summary>
    /// Adds the resilience (retry behavior) behaviors to the CQRS pipeline.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the CQRS services will be added.</param>
    /// <param name="configuration">The <see cref="IConfiguration"/> instance containing the configuration for the retry behavior.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsResilienceBehavior([DisallowNull] this IServiceCollection services, [DisallowNull] IConfiguration configuration) {
        var options = configuration
                   .GetSection(RetryBehaviorOptions.SectionName)
                   .Get<RetryBehaviorOptions>()
                   ?? new RetryBehaviorOptions();
        AddCqrsResilienceBehavior(services, options);
        return services;
    }
    /// <summary>
    /// Adds the resilience (retry behavior) behaviors to the CQRS pipeline.
    /// </summary>
    /// <remarks>
    /// The behavior is registered as a singleton per closed request type: the circuit breaker
    /// owns cross-dispatch state, so a transient registration would hand every dispatch a
    /// fresh breaker that can never trip.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the CQRS services will be added.</param>
    /// <param name="options">The <see cref="RetryBehaviorOptions"/> instance containing the configuration for the retry behavior.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsResilienceBehavior([DisallowNull] this IServiceCollection services, [DisallowNull] RetryBehaviorOptions options) {
        options ??= new();

        services.Configure<RetryBehaviorOptions>(
            opts => {
                opts.RetryCount = options.RetryCount;
                opts.BaseDelayMilliseconds = options.BaseDelayMilliseconds;
                opts.EnableCircuitBreaker = options.EnableCircuitBreaker;
                opts.TimeoutSeconds = options.TimeoutSeconds;
                opts.CircuitBreakerFailureThreshold = options.CircuitBreakerFailureThreshold;
                opts.CircuitBreakerOpenSeconds = options.CircuitBreakerOpenSeconds;
            }
        );

        // Shared by every breaker instance: health checks and diagnostics read through it.
        // Last registration wins, so hosts can substitute their own registry.
        services.TryAddSingleton<CircuitBreakerRegistry>();
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(RetryBehavior<,>));

        return services;
    }

    /// <summary>
    /// Adds the resilience (rate-limit behavior) to the CQRS pipeline.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the CQRS services will be added.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsRateLimiting([DisallowNull] this IServiceCollection services) => AddCqrsRateLimiting(services, new RateLimitOptions());

    /// <summary>
    /// Adds the resilience (rate-limit behavior) to the CQRS pipeline.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the CQRS services will be added.</param>
    /// <param name="configuration">The <see cref="IConfiguration"/> instance containing the configuration for the rate limiter.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsRateLimiting([DisallowNull] this IServiceCollection services, [DisallowNull] IConfiguration configuration) {
        var options = configuration
                   .GetSection(RateLimitOptions.SectionName)
                   .Get<RateLimitOptions>()
                   ?? new RateLimitOptions();
        AddCqrsRateLimiting(services, options);
        return services;
    }

    /// <summary>
    /// Adds the resilience (rate-limit behavior) to the CQRS pipeline.
    /// </summary>
    /// <remarks>
    /// The partitioned limiter is shared by every behavior instance: rate limiting is
    /// cross-dispatch state, so a per-dispatch limiter could never trip. Register your own
    /// <see cref="PartitionedRateLimiter{TResource}"/> first to substitute the built-in one.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the CQRS services will be added.</param>
    /// <param name="options">The <see cref="RateLimitOptions"/> instance containing the configuration for the rate limiter.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsRateLimiting([DisallowNull] this IServiceCollection services, [DisallowNull] RateLimitOptions options) {
        options ??= new();

        services.Configure<RateLimitOptions>(
            opts => {
                opts.PermitLimit = options.PermitLimit;
                opts.Window = options.Window;
                opts.QueueLimit = options.QueueLimit;
                opts.KeySelector = options.KeySelector;
            }
        );

        services.TryAddSingleton(_ => PartitionedRateLimiter.Create<string, string>(
            key => RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions {
                PermitLimit = options.PermitLimit,
                Window = options.Window,
                QueueLimit = options.QueueLimit,
                AutoReplenishment = true
            }),
            EqualityComparer<string>.Default));

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RateLimitBehavior<,>));

        return services;
    }

    /// <summary>
    /// Adds per-key concurrency partitioning to the CQRS pipeline with default options.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the CQRS services will be added.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsPartitionedConcurrency([DisallowNull] this IServiceCollection services) =>
        AddCqrsPartitionedConcurrency(services, new PartitionedConcurrencyOptions());

    /// <summary>
    /// Adds per-key concurrency partitioning to the CQRS pipeline.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the CQRS services will be added.</param>
    /// <param name="configuration">The <see cref="IConfiguration"/> instance containing the partitioning options.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsPartitionedConcurrency([DisallowNull] this IServiceCollection services, [DisallowNull] IConfiguration configuration) {
        var options = configuration
                   .GetSection(PartitionedConcurrencyOptions.SectionName)
                   .Get<PartitionedConcurrencyOptions>()
                   ?? new PartitionedConcurrencyOptions();

        return AddCqrsPartitionedConcurrency(services, options);
    }

    /// <summary>
    /// Adds per-key concurrency partitioning to the CQRS pipeline.
    /// </summary>
    /// <remarks>
    /// The registry is shared by every behavior instance: concurrency limits are cross-dispatch
    /// state, so a per-dispatch registry could never serialize anything. Register your own
    /// <see cref="PartitionedConcurrencyRegistry"/> first to substitute the built-in one.
    /// Waiting semantics: by default a dispatch waits for its key; set
    /// <see cref="PartitionedConcurrencyOptions.FailFastWhenBusy"/> or
    /// <see cref="PartitionedConcurrencyOptions.WaitTimeout"/> to shed or bound the wait.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the CQRS services will be added.</param>
    /// <param name="options">The <see cref="PartitionedConcurrencyOptions"/> instance containing the partitioning configuration.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsPartitionedConcurrency([DisallowNull] this IServiceCollection services, [DisallowNull] PartitionedConcurrencyOptions options) {
        options ??= new();

        services.Configure<PartitionedConcurrencyOptions>(
            opts => {
                opts.ConcurrencyLimit = options.ConcurrencyLimit;
                opts.MaxPartitions = options.MaxPartitions;
                opts.FailFastWhenBusy = options.FailFastWhenBusy;
                opts.WaitTimeout = options.WaitTimeout;
                opts.KeySelector = options.KeySelector;
            }
        );

        services.TryAddSingleton<PartitionedConcurrencyRegistry>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PartitionedConcurrencyBehavior<,>));

        return services;
    }
}
