//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Caching;

/// <summary>
/// In-process single-flight guard: concurrent misses for one cache key wait for the first
/// caller, then re-check the cache instead of stampeding the handler.
/// </summary>
/// <remarks>
/// Gates are reference-counted (waiting + holding callers) and removed when the last caller
/// leaves, so the map does not grow with the key space. The guard is process-local by design:
/// it prevents a per-instance stampede, not a cross-instance one.
/// </remarks>
public sealed class CacheStampedeGuard {
    private readonly Lock _sync = new();
    private readonly Dictionary<string, Gate> _gates = new(StringComparer.Ordinal);

    /// <summary>
    /// Wait for the key's turn and returns a lease that must be disposed to release it.
    /// Cancellation while waiting leaves the map exactly as it was.
    /// </summary>
    /// <param name="key">The full cache key.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    public async Task<CacheKeyLease> EnterAsync(string key, CancellationToken cancellationToken) {
        Guard.NotNullOrEmpty(key);

        Gate gate;
        lock(_sync) {
            if(!_gates.TryGetValue(key, out gate!)) {
                gate = new Gate();
                _gates.Add(key, gate);
            }

            gate.Users++;
        }

        try {
            await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch {
            LeaveWaiting(key, gate);

            throw;
        }

        return new CacheKeyLease(this, key, gate);
    }

    private void Release(string key, Gate gate) {
        gate.Semaphore.Release();

        lock(_sync) {
            gate.Users--;

            if(gate.Users is 0) {
                _gates.Remove(key);
            }
        }
    }

    private void LeaveWaiting(string key, Gate gate) {
        lock(_sync) {
            gate.Users--;

            if(gate.Users is 0) {
                _gates.Remove(key);
            }
        }
    }

    internal sealed class Gate {
        internal SemaphoreSlim Semaphore { get; } = new(1, 1);

        internal int Users { get; set; }
    }

    /// <summary>
    /// A single-flight permit. Disposing it releases the key; double disposal is a no-op.
    /// </summary>
    public sealed class CacheKeyLease : IDisposable {
        private readonly CacheStampedeGuard _guard;
        private readonly string _key;
        private readonly Gate _gate;
        private int _released;

        internal CacheKeyLease(CacheStampedeGuard guard, string key, Gate gate) {
            _guard = guard;
            _key = key;
            _gate = gate;
        }

        /// <inheritdoc />
        public void Dispose() {
            if(Interlocked.Exchange(ref _released, 1) is 0) {
                _guard.Release(_key, _gate);
            }
        }
    }
}