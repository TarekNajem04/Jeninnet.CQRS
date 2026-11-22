//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Immutable policy for one receive endpoint: prefetch, concurrency, retry, dead-letter routing,
/// consumption timeout, and endpoint-scoped receive filters. Every transport adapter consumes the
/// same object; broker-specific escape hatches stay in the package options.
/// </summary>
/// <remarks>
/// The configuration is a value type on purpose: create it with
/// <see cref="EndpointConfigurationBuilder{TMessage}"/> (or <c>with</c> expressions), validate it
/// once at startup, and share the validated instance. Validation deliberately happens outside the
/// constructor so a typo can be reported with the endpoint name when the host starts.
/// </remarks>
public sealed record EndpointConfiguration {
    /// <summary>Gets the unique endpoint name used by filters and dead-letter routing.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the maximum number of unacknowledged deliveries the broker may hand this endpoint.
    /// Zero means the transport default. Defaults to 16.
    /// </summary>
    public int PrefetchCount { get; init; } = 16;

    /// <summary>Gets the maximum number of concurrent handler invocations for this endpoint.</summary>
    public int ConcurrentLimit { get; init; } = 1;

    /// <summary>Gets the retry policy applied before the delivery is acknowledged or rejected.</summary>
    public EndpointRetryPolicy Retry { get; init; } = EndpointRetryPolicy.None;

    /// <summary>
    /// Gets the queue or endpoint that receives rejected deliveries, or <see langword="null"/>
    /// when the broker's convention (for example <c>{queue}.dlq</c>) is used.
    /// </summary>
    public string? DeadLetterTarget { get; init; }

    /// <summary>
    /// Gets the per-delivery handler budget. Defaults to
    /// <see cref="Timeout.InfiniteTimeSpan"/>; a finite value cancels the handler and surfaces a
    /// <see cref="TimeoutException"/> so the transport can reject or retry the delivery.
    /// </summary>
    public TimeSpan ConsumptionTimeout { get; init; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Gets the filters declared directly on this endpoint, in declaration order. Global filters
    /// registered for the message type run before them; see <see cref="IReceiveFilter{TMessage}"/>.
    /// </summary>
    public IReadOnlyList<Type> Filters { get; init; } = [];

    /// <summary>
    /// Validates this configuration and throws an <see cref="EndpointConfigurationException"/>
    /// naming this endpoint when a value is invalid.
    /// </summary>
    /// <exception cref="EndpointConfigurationException">A value is outside its permitted range.</exception>
    public void Validate() {
        var name = Name ?? string.Empty;

        if(StringCompat.IsNullOrWhiteSpace(name)) {
            throw new EndpointConfigurationException(
                name,
                "Receive endpoint configuration requires a non-empty name.");
        }

        if(PrefetchCount < 0) {
            throw new EndpointConfigurationException(
                name,
                $"Endpoint '{name}' has invalid prefetch count {PrefetchCount}; prefetch must be zero or greater.");
        }

        if(ConcurrentLimit < 1) {
            throw new EndpointConfigurationException(
                name,
                $"Endpoint '{name}' has invalid concurrent limit {ConcurrentLimit}; the limit must be at least 1.");
        }

        if(ConsumptionTimeout != Timeout.InfiniteTimeSpan && ConsumptionTimeout <= TimeSpan.Zero) {
            throw new EndpointConfigurationException(
                name,
                $"Endpoint '{name}' has invalid consumption timeout {ConsumptionTimeout}; use Timeout.InfiniteTimeSpan or a positive duration.");
        }

        if(DeadLetterTarget is not null) {
            if(StringCompat.IsNullOrWhiteSpace(DeadLetterTarget)) {
                throw new EndpointConfigurationException(
                    name,
                    $"Endpoint '{name}' declares an empty dead-letter target.");
            }

            if(string.Equals(DeadLetterTarget, name, StringComparison.Ordinal)) {
                throw new EndpointConfigurationException(
                    name,
                    $"Endpoint '{name}' cannot use itself as its dead-letter target.");
            }
        }

        Retry.Validate(name);
    }

    /// <summary>
    /// Creates the default policy for a message type that has no explicit endpoint configuration:
    /// transport defaults, no retry, no timeout, and no filters.
    /// </summary>
    /// <param name="messageType">The message type the endpoint consumes.</param>
    /// <returns>The default configuration named after <paramref name="messageType"/>.</returns>
    public static EndpointConfiguration DefaultFor(Type messageType) {
        Guard.NotNull(messageType);

        return new EndpointConfiguration { Name = messageType.Name };
    }
}