//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Recurring;

/// <summary>
/// In-process overlap guard for recurring schedules: one execution per unique key. The skip
/// policy returns null immediately when busy; the queue policy waits. Gates are refcounted and
/// removed when the last waiter/holder leaves.
/// </summary>
public sealed class RecurringExecutionGate {
    private readonly Lock _sync = new();
    private readonly Dictionary<string, Gate> _gates = new(StringComparer.Ordinal);

    /// <summary>
    /// Enters the schedule's execution slot according to <paramref name="policy"/>.
    /// </summary>
    /// <param name="uniqueKey">The schedule key.</param>
    /// <param name="policy">Skip or queue.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>
    /// A lease to dispose when the execution finishes, or <see langword="null"/> when the skip
    /// policy found the key busy.
    /// </returns>
    public async Task<IDisposable?> EnterAsync(string uniqueKey, RecurringOverlapPolicy policy, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(uniqueKey);

        Gate gate;
        lock(_sync) {
            if(!_gates.TryGetValue(uniqueKey, out gate!)) {
                gate = new Gate();
                _gates.Add(uniqueKey, gate);
            }

            gate.Users++;
        }

        if(policy is RecurringOverlapPolicy.Skip) {
            if(!await gate.Semaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false)) {
                LeaveWaiting(uniqueKey, gate);

                return null;
            }
        } else {
            try {
                await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch {
                LeaveWaiting(uniqueKey, gate);

                throw;
            }
        }

        return new Lease(this, uniqueKey, gate);
    }

    private void Release(string uniqueKey, Gate gate) {
        gate.Semaphore.Release();

        lock(_sync) {
            gate.Users--;

            if(gate.Users is 0) {
                _gates.Remove(uniqueKey);
            }
        }
    }

    private void LeaveWaiting(string uniqueKey, Gate gate) {
        lock(_sync) {
            gate.Users--;

            if(gate.Users is 0) {
                _gates.Remove(uniqueKey);
            }
        }
    }

    private sealed class Gate {
        internal SemaphoreSlim Semaphore { get; } = new(1, 1);

        internal int Users { get; set; }
    }

    private sealed class Lease(RecurringExecutionGate gate, string key, Gate inner) : IDisposable {
        private int _disposed;

        public void Dispose() {
            if(Interlocked.Exchange(ref _disposed, 1) is 0) {
                gate.Release(key, inner);
            }
        }
    }
}
