//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Fluent builder for one <see cref="EndpointConfiguration"/>. Values are validated at startup,
/// not while the builder is being configured, so callers see every mistake in one pass with the
/// endpoint name attached.
/// </summary>
/// <typeparam name="TMessage">The message type the endpoint consumes.</typeparam>
public sealed class EndpointConfigurationBuilder<TMessage> {
    private readonly List<Type> _filters = [];
    private string? _name;
    private int _prefetchCount = 16;
    private int _concurrentLimit = 1;
    private EndpointRetryPolicy _retry = EndpointRetryPolicy.None;
    private string? _deadLetterTarget;
    private TimeSpan _consumptionTimeout = Timeout.InfiniteTimeSpan;

    /// <summary>Sets the unique endpoint name. Defaults to the message type's simple name.</summary>
    /// <param name="name">The endpoint name used by filters and dead-letter routing.</param>
    /// <returns>This builder.</returns>
    public EndpointConfigurationBuilder<TMessage> Named(string name) {
        _name = name;

        return this;
    }

    /// <summary>Sets the broker prefetch count. Zero means the transport default.</summary>
    /// <param name="prefetchCount">The maximum unacknowledged delivery count.</param>
    /// <returns>This builder.</returns>
    public EndpointConfigurationBuilder<TMessage> WithPrefetch(int prefetchCount) {
        _prefetchCount = prefetchCount;

        return this;
    }

    /// <summary>Sets the maximum number of concurrent handler invocations.</summary>
    /// <param name="concurrentLimit">The concurrency limit; at least 1.</param>
    /// <returns>This builder.</returns>
    public EndpointConfigurationBuilder<TMessage> WithConcurrency(int concurrentLimit) {
        _concurrentLimit = concurrentLimit;

        return this;
    }

    /// <summary>Sets the receive-side retry policy.</summary>
    /// <param name="retry">The retry policy.</param>
    /// <returns>This builder.</returns>
    public EndpointConfigurationBuilder<TMessage> WithRetry(EndpointRetryPolicy retry) {
        _retry = retry ?? throw new ArgumentNullException(nameof(retry));

        return this;
    }

    /// <summary>Sets a constant or exponential retry policy.</summary>
    /// <param name="maxAttempts">Total attempts, including the first; at least 1.</param>
    /// <param name="delay">The delay before the second attempt.</param>
    /// <param name="backoffMultiplier">The delay multiplier per failure; 1 is constant backoff.</param>
    /// <returns>This builder.</returns>
    public EndpointConfigurationBuilder<TMessage> WithRetry(int maxAttempts, TimeSpan delay, double backoffMultiplier = 2.0) =>
        WithRetry(new EndpointRetryPolicy {
            MaxAttempts = maxAttempts,
            Delay = delay,
            BackoffMultiplier = backoffMultiplier,
        });

    /// <summary>Sets the dead-letter target; <see langword="null"/> keeps the broker convention.</summary>
    /// <param name="target">The dead-letter queue or endpoint name.</param>
    /// <returns>This builder.</returns>
    public EndpointConfigurationBuilder<TMessage> WithDeadLetterTarget(string? target) {
        _deadLetterTarget = target;

        return this;
    }

    /// <summary>Sets the per-delivery handler budget.</summary>
    /// <param name="timeout">The timeout, or <see cref="Timeout.InfiniteTimeSpan"/> for none.</param>
    /// <returns>This builder.</returns>
    public EndpointConfigurationBuilder<TMessage> WithConsumptionTimeout(TimeSpan timeout) {
        _consumptionTimeout = timeout;

        return this;
    }

    /// <summary>Adds a filter that runs only for this endpoint.</summary>
    /// <typeparam name="TFilter">The filter type; resolved from DI per delivery.</typeparam>
    /// <returns>This builder.</returns>
    public EndpointConfigurationBuilder<TMessage> WithFilter<TFilter>()
        where TFilter : class, IReceiveFilter<TMessage> {
        _filters.Add(typeof(TFilter));

        return this;
    }

    internal EndpointConfiguration Build() =>
        new() {
            Name = _name ?? typeof(TMessage).Name,
            PrefetchCount = _prefetchCount,
            ConcurrentLimit = _concurrentLimit,
            Retry = _retry,
            DeadLetterTarget = _deadLetterTarget,
            ConsumptionTimeout = _consumptionTimeout,
            Filters = [.. _filters],
        };
}