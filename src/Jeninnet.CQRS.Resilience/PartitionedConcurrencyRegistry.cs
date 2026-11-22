//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Resilience;

/// <summary>
/// Process-wide map of per-key concurrency gates. A partition is created on first use and owns
/// one <see cref="SemaphoreSlim"/> sized by
/// <see cref="PartitionedConcurrencyOptions.ConcurrencyLimit"/>.
/// </summary>
/// <remarks>
/// Eviction is size-triggered and least-recently-used first: whenever the map exceeds
/// <see cref="PartitionedConcurrencyOptions.MaxPartitions"/>, idle partitions (no waiting or
/// running dispatch) are removed oldest-first. Active partitions are never evicted, so the bound
/// is a soft watermark rather than a hard cap; idle entries cannot leak.
/// </remarks>
public sealed class PartitionedConcurrencyRegistry {
    private readonly object _gate = new(); // NOSONAR: System.Threading.Lock is unavailable on netstandard2.0/net462; the object lock is the compatible shim.
    private readonly Dictionary<string, Partition> _partitions = new(StringComparer.Ordinal);
    private readonly PartitionedConcurrencyOptions _options;

    /// <summary>
    /// Initializes the registry from options.
    /// </summary>
    /// <param name="options">The partitioning options; never <see langword="null"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <see cref="PartitionedConcurrencyOptions.ConcurrencyLimit"/> or
    /// <see cref="PartitionedConcurrencyOptions.MaxPartitions"/> is less than 1.
    /// </exception>
    public PartitionedConcurrencyRegistry(IOptions<PartitionedConcurrencyOptions> options) {
        Guard.NotNull(options);
        _options = options.Value;

        if(_options.ConcurrencyLimit < 1) {
            throw new ArgumentOutOfRangeException(nameof(options), _options.ConcurrencyLimit, "ConcurrencyLimit must be at least 1.");
        }

        if(_options.MaxPartitions < 1) {
            throw new ArgumentOutOfRangeException(nameof(options), _options.MaxPartitions, "MaxPartitions must be at least 1.");
        }
    }

    /// <summary>Gets the number of live partitions, both idle and active.</summary>
    public int Count {
        get {
            lock(_gate) {
                return _partitions.Count;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether a live partition exists for <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The partition key.</param>
    public bool ContainsKey(string key) {
        lock(_gate) {
            return _partitions.ContainsKey(key);
        }
    }

    /// <summary>
    /// Waits for a permit in the partition named by <paramref name="key"/> and returns a lease
    /// that must be disposed to release it. Cancellation while waiting leaves the partition
    /// exactly as it was: only acquired permits are released.
    /// </summary>
    /// <param name="key">The partition key.</param>
    /// <param name="cancellationToken">A token to stop waiting.</param>
    /// <exception cref="PartitionBusyException">
    /// Thrown when the partition is busy and fail-fast or a wait timeout applies.
    /// </exception>
    public async ValueTask<PartitionLease> AcquireAsync(string key, CancellationToken cancellationToken) {
        Guard.NotNullOrEmpty(key);

        var partition = Rent(key);
        var acquired = false;

        try {
            acquired = await WaitAsync(partition, cancellationToken).ConfigureAwait(false);
        }
        finally {
            if(!acquired) {
                Return(partition);
            }
        }

        return acquired
            ? new PartitionLease(this, partition, key)
            : throw new PartitionBusyException(key);
    }

    internal void Release(Partition partition) {
        partition.Semaphore.Release();
        Return(partition);
    }

    private async ValueTask<bool> WaitAsync(Partition partition, CancellationToken cancellationToken) {
        if(_options.FailFastWhenBusy) {
            return await partition.Semaphore.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
        }

        if(_options.WaitTimeout is { } timeout) {
            return await partition.Semaphore.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }

        await partition.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    // A caller is counted as a user before it touches the semaphore and until it returns the
    // permit (or abandons the wait), so only partitions with Users == 0 may be evicted.
    private Partition Rent(string key) {
        lock(_gate) {
            if(!_partitions.TryGetValue(key, out var partition)) {
                partition = new Partition(_options.ConcurrencyLimit);
                _partitions.Add(key, partition);
            }

            partition.Users++;
            partition.LastUsedTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            EvictLocked();

            return partition;
        }
    }

    private void Return(Partition partition) {
        lock(_gate) {
            partition.Users--;
            partition.LastUsedTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            EvictLocked();
        }
    }

    private void EvictLocked() {
        if(_partitions.Count <= _options.MaxPartitions) {
            return;
        }

        var byAge = _partitions.OrderBy(static pair => pair.Value.LastUsedTicks).ToList();

        foreach(var pair in byAge) {
            if(_partitions.Count <= _options.MaxPartitions) {
                break;
            }

            if(pair.Value.Users is 0) {
                _partitions.Remove(pair.Key);
            }
        }
    }

    internal sealed class Partition(int limit) {
        internal SemaphoreSlim Semaphore { get; } = new(limit, limit);

        internal int Users { get; set; }

        internal long LastUsedTicks { get; set; }
    }
}
