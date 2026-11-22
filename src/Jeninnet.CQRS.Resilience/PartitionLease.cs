//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Resilience;

/// <summary>
/// A permit held in a <see cref="PartitionedConcurrencyRegistry"/> partition. Disposing it
/// releases the permit exactly once; disposing twice is a no-op.
/// </summary>
public sealed class PartitionLease : IDisposable {
    private readonly PartitionedConcurrencyRegistry _registry;
    private readonly PartitionedConcurrencyRegistry.Partition _partition;
    private int _released;

    internal PartitionLease(PartitionedConcurrencyRegistry registry, PartitionedConcurrencyRegistry.Partition partition, string key) {
        _registry = registry;
        _partition = partition;
        Key = key;
    }

    /// <summary>Gets the partition key this lease belongs to.</summary>
    public string Key { get; }

    /// <inheritdoc />
    public void Dispose() {
        if(Interlocked.Exchange(ref _released, 1) is 0) {
            _registry.Release(_partition);
        }
    }
}
