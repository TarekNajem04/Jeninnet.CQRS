//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Resilience;

/// <summary>
/// Process-wide registry of circuit-breaker state readers. Behaviors self-register on
/// construction so health checks and diagnostics can observe them without resolving every
/// closed generic from the container.
/// </summary>
public sealed class CircuitBreakerRegistry {
    private readonly ConcurrentDictionary<string, Func<CircuitBreakerSnapshot>> _readers = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers (or replaces) the snapshot reader for the named breaker.
    /// </summary>
    /// <param name="name">The breaker name, typically the request type full name.</param>
    /// <param name="read">A function returning the current snapshot. Must be thread-safe.</param>
    public void Register(string name, Func<CircuitBreakerSnapshot> read) {
        Guard.NotNullOrEmpty(name);
        Guard.NotNull(read);

        _readers[name] = read;
    }

    /// <summary>
    /// Gets a point-in-time snapshot of every registered breaker.
    /// </summary>
    public IReadOnlyDictionary<string, CircuitBreakerSnapshot> SnapshotAll() =>
        _readers.ToDictionary(static entry => entry.Key, static entry => entry.Value(), StringComparer.Ordinal);
}
