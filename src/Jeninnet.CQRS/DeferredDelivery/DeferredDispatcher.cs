//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.DeferredDelivery;

/// <summary>
/// In-process deferred delivery: holds commands until they are due, then dispatches each
/// through a fresh scope. Every deferral is staged in the <see cref="IDeferredStore"/>
/// before it is queued, so a crash loses nothing accepted; completed, cancelled, and
/// expired entries never replay.
/// </summary>
/// <remarks>
/// Initializes a new instance with the given scope factory, clock, store, options, and relay.
/// </remarks>
/// <param name="scopes">Used to open one scope per deferred execution. Never <see langword="null"/>.</param>
/// <param name="clock">The clock used for due-time math and waiting. Never <see langword="null"/>.</param>
/// <param name="store">The durability log. Never <see langword="null"/>.</param>
/// <param name="options">The delivery options. Never <see langword="null"/>.</param>
/// <param name="relay">Executes each envelope. Never <see langword="null"/>.</param>
/// <param name="deadLetters">Receives failures and owns replay removal. Never <see langword="null"/>.</param>
/// <param name="tenant">The ambient tenant adapter; <see langword="null"/> without tenancy.</param>
public sealed class DeferredDispatcher(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    IDeferredStore store,
    IOptions<DeferredDeliveryOptions> options,
    IMessageRelay relay,
    IDeadLetterStore deadLetters,
    TenantAccessor? tenant = null) : IDeferredDispatcher, IHostedService, IDisposable {
    private readonly IServiceScopeFactory _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly IDeferredStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly DeferredDeliveryOptions _options = ValidateOptions((options ?? throw new ArgumentNullException(nameof(options))).Value);
    private readonly IMessageRelay _relay = relay ?? throw new ArgumentNullException(nameof(relay));
    private readonly IDeadLetterStore _deadLetters = deadLetters ?? throw new ArgumentNullException(nameof(deadLetters));
    private readonly TenantAccessor? _tenant = tenant;

    private readonly Lock _sync = new();
    private readonly PriorityQueue<DeferredItem, (DateTimeOffset Due, long Sequence)> _queue = new();
    private readonly CancellationTokenSource _execution = new();
    private long _sequence;
    private CancellationTokenSource _wakeup = new();
    private CancellationTokenSource? _stopping;
    private Task? _pump;
    private bool _disposed;
    private bool _draining;

    /// <inheritdoc />
    public event EventHandler<DeferredFaultEventArgs>? Faulted;

    /// <inheritdoc />
    public async Task DeferAsync<TCommand>(TCommand command, TimeSpan delay, CancellationToken cancellationToken = default)
        where TCommand : ICommand {
        Guard.NotNull(command);
        EnsureAcceptingWork();

        var now = _clock.GetUtcNow();
        var envelope = new DeferredEnvelope<TCommand>(
            command,
            now + (delay < TimeSpan.Zero ? TimeSpan.Zero : delay),
            _options.TimeToLive is null ? null : now + _options.TimeToLive) {
            TenantId = _tenant?.Current,
        };
        var key = await _store.StageAsync(envelope, cancellationToken).ConfigureAwait(false);

        Enqueue(key, envelope, cancellationToken); // NOSONAR: The synchronous API is intentional for netstandard2.0/net462 compatibility.
    }

    /// <inheritdoc />
    public async Task DeferAsync<TCommand, TResult>(TCommand command, TimeSpan delay, CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult> {
        Guard.NotNull(command);
        EnsureAcceptingWork();

        var now = _clock.GetUtcNow();
        var envelope = new DeferredResultEnvelope<TCommand, TResult>(
            command,
            now + (delay < TimeSpan.Zero ? TimeSpan.Zero : delay),
            _options.TimeToLive is null ? null : now + _options.TimeToLive) {
            TenantId = _tenant?.Current,
        };
        var key = await _store.StageAsync(envelope, cancellationToken).ConfigureAwait(false);

        Enqueue(key, envelope, cancellationToken); // NOSONAR: The synchronous API is intentional for netstandard2.0/net462 compatibility.
    }

    /// <inheritdoc />
    public async Task<bool> ReplayAsync(string deadLetterKey, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(deadLetterKey);

        var entry = await _deadLetters.GetAsync(deadLetterKey, cancellationToken).ConfigureAwait(false);

        if(entry is null) {
            return false;
        }

        if(entry.Envelope is UnresolvedCommandEnvelope) {
            // There is no command instance to re-stage: the type is still missing. Keep the
            // dead letter for inspection rather than staging a String placeholder row.
            return false;
        }

        // DueUtc delegates to the original (now overdue) envelope, so the pump runs it on the
        // next iteration. Expiry is overridden by the wrapper: replay is explicit.
        var envelope = new DeadLetterReplayEnvelope(entry.Envelope, deadLetterKey);
        var key = await _store.StageAsync(envelope, cancellationToken).ConfigureAwait(false);

        Enqueue(key, envelope, cancellationToken); // NOSONAR: The synchronous API is intentional for netstandard2.0/net462 compatibility.

        return true;
    }

    /// <inheritdoc />
    public Task EnqueueAsync(string key, IDeferredEnvelope envelope, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);
        Guard.NotNull(envelope);

        lock(_sync) {
            if(_pump is null) {
                // Not started yet: recovery will list the staged entry. Enqueuing now would
                // duplicate it when StartAsync replays the store.
                return Task.CompletedTask;
            }
        }

        Enqueue(key, envelope, cancellationToken);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken) {
        lock(_sync) {
            Guard.NotDisposed(_disposed, this);

            if(_pump is not null) {
                return;
            }

            _stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        // Recovery runs before the pump starts consuming, so replayed entries keep
        // their original due times. No-op for stores without cross-restart state.
        if(_options.EnableRecovery) {
            var pending = await _store.ListAsync(cancellationToken).ConfigureAwait(false);

            foreach(var (key, envelope) in pending) {
                Enqueue(key, envelope, CancellationToken.None); // NOSONAR: The synchronous API is intentional for netstandard2.0/net462 compatibility.
            }
        }

        lock(_sync) {
            Guard.NotDisposed(_disposed, this);

            // Task.Run assigns _pump before any pump code runs: the synchronous prefix of an
            // async method could otherwise stage the next occurrence while _pump is still null.
            _pump ??= Task.Run(() => PumpAsync(_stopping.Token), CancellationToken.None);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken) {
        Task? pump;
        CancellationTokenSource? stopping;

        lock(_sync) {
            _draining = true;
            pump = _pump;
            stopping = _stopping;
        }

        if(pump is null) {
            return;
        }

        // Stop accepting new work and wake the pump, but keep the execution token alive so the
        // item already running can finish. Remaining staged items stay for the next start.
        stopping?.Cancel(); // NOSONAR: The synchronous API is intentional for netstandard2.0/net462 compatibility.

        if(_options.DrainTimeout > TimeSpan.Zero) {
            var delay = _clock.DelayAsync(_options.DrainTimeout, cancellationToken);
            var completed = await Task.WhenAny(pump, delay).ConfigureAwait(false);

            if(completed == pump) {
                await ObservePumpAsync(pump, cancellationToken).ConfigureAwait(false);

                return;
            }

            if(cancellationToken.IsCancellationRequested) {
                _execution.Cancel(); // NOSONAR: The synchronous API is intentional for netstandard2.0/net462 compatibility.
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        // Deadline elapsed (or draining is disabled): cancel the in-flight handler and return
        // promptly. The staged entry survives, so the next start replays it (at-least-once).
        _execution.Cancel(); // NOSONAR: The synchronous API is intentional for netstandard2.0/net462 compatibility.
    }

    private static async Task ObservePumpAsync(Task pump, CancellationToken cancellationToken) {
        try {
            await pump.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch(OperationCanceledException) {
            // Either the pump stopped (expected) or the caller gave up waiting.
            // Only the caller's cancellation escapes.
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        CancellationTokenSource? stopping;

        lock(_sync) {
            if(_disposed) {
                return;
            }

            _disposed = true;
            stopping = _stopping;
        }

        stopping?.Cancel();
        stopping?.Dispose();
        _execution.Cancel();
        _execution.Dispose();

        // Call StopAsync before Dispose in normal hosting; the pump is already gone by then.
        lock(_sync) {
            _wakeup.Dispose();
        }
    }

    private void Enqueue(string key, IDeferredEnvelope envelope, CancellationToken cancellationToken) {
        CancellationTokenSource wake;
        lock(_sync) {
            Guard.NotDisposed(_disposed, this);

            if(_draining) {
                throw new InvalidOperationException("Deferred delivery is stopping; new deferrals are rejected.");
            }

            _queue.Enqueue(new DeferredItem(key, envelope, cancellationToken), (envelope.DueUtc, _sequence++));
            wake = Interlocked.Exchange(ref _wakeup, new CancellationTokenSource());
        }

        // Wake the pump so an earlier due time is honored; a spurious wake-up only re-reads the queue.
        wake.Cancel();
        wake.Dispose();
    }

    private void EnsureAcceptingWork() {
        lock(_sync) {
            Guard.NotDisposed(_disposed, this);

            if(_draining) {
                throw new InvalidOperationException("Deferred delivery is stopping; new deferrals are rejected.");
            }
        }
    }

    private static DeferredDeliveryOptions ValidateOptions(DeferredDeliveryOptions options) {
        if(options.DrainTimeout < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options), options.DrainTimeout, "DrainTimeout must not be negative.");
        }

        return options;
    }

    private static readonly TimeSpan _backstopInterval = TimeSpan.FromMilliseconds(50);

    private async Task PumpAsync(CancellationToken stoppingToken) { // NOSONAR: Complexity is inherent to this state machine/pipeline and is covered by tests.
        while(!stoppingToken.IsCancellationRequested) {
            DeferredItem? next = null;
            var wait = Timeout.InfiniteTimeSpan;
            CancellationToken wakeToken;

            lock(_sync) {
                // Capture the queue and wake token under one lock: an enqueue between two
                // separate reads could swap the source first and leave this iteration asleep.
                wakeToken = _wakeup.Token;

                if(_queue.TryPeek(out var item, out var priority)) {
                    next = item;
                    wait = priority.Due - _clock.GetUtcNow();
                }
            }

            if(next is null || wait > TimeSpan.Zero) {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, wakeToken);
                var delay = _clock.DelayAsync(next is null ? Timeout.InfiniteTimeSpan : wait, linked.Token);

                if(next is not null && !ReferenceEquals(_clock, TimeProvider.System)) {
                    // Fake clocks stand still between advances: a delay armed from a stale
                    // snapshot (test thread advanced past the due time mid-arming) would sleep
                    // forever, as no further advance is coming. Re-check on a bounded real-time
                    // backstop instead of trusting one shot; a fresh snapshot either drains an
                    // overdue head or re-arms. Production (TimeProvider.System) keeps pure
                    // event-driven waits.
                    var backstop = Task.Delay(_backstopInterval, stoppingToken);
                    var first = await Task.WhenAny(delay, backstop).ConfigureAwait(false);

                    if(first != delay) {
                        continue; // Re-snapshot with a fresh clock reading.
                    }
                }

                try {
                    await delay.ConfigureAwait(false);
                }
                catch(OperationCanceledException) when(!stoppingToken.IsCancellationRequested) {
                    continue; // A new (possibly earlier) item arrived; re-snapshot.
                }
            }

            var batch = new List<DeferredItem>();
            var now = _clock.GetUtcNow();
            lock(_sync) {
                while(_queue.Count > 0 && _queue.TryPeek(out _, out var priority) && priority.Due <= now) {
                    batch.Add(_queue.Dequeue());
                }
            }

            foreach(var item in batch) {
                // Stop accepting new work: the current item may finish, the rest stay staged.
                if(stoppingToken.IsCancellationRequested) {
                    return;
                }

                if(item.Cancellation.IsCancellationRequested || (item.Envelope.ExpiresAtUtc is not null && item.Envelope.ExpiresAtUtc <= now)) {
                    // Cancelled and expired deferrals never run — and must not replay,
                    // so the staged entry is completed instead of left behind.
                    await CompleteAsync(item.Key, _execution.Token).ConfigureAwait(false);

                    continue;
                }

                using var tenantScope = _tenant?.EnterScope(item.Envelope.TenantId);

                try {
                    using var scope = _scopes.CreateScope();
                    await _relay.RelayAsync(item.Envelope, scope.ServiceProvider, _execution.Token).ConfigureAwait(false);
                    await CompleteReplayAsync(item.Envelope, _execution.Token).ConfigureAwait(false);
                    await CompleteAsync(item.Key, _execution.Token).ConfigureAwait(false);
                }
                catch(OperationCanceledException) when(_execution.IsCancellationRequested || stoppingToken.IsCancellationRequested) {
                    return; // Host is stopping; remaining items are abandoned with it.
                }
                catch(Exception exception) {
                    var failedAtUtc = _clock.GetUtcNow();
                    Faulted?.Invoke(this, new DeferredFaultEventArgs(item.Envelope.Command.GetType().FullName ?? item.Envelope.Command.GetType().Name, exception, failedAtUtc));
                    await MoveToDeadLetterAsync(item, exception, failedAtUtc, _execution.Token).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task CompleteAsync(string key, CancellationToken stoppingToken) {
        if(stoppingToken.IsCancellationRequested) {
            return; // Drain deadline hit: keep the staged entry for the next start.
        }

        try {
            await _store.CompleteAsync(key, stoppingToken).ConfigureAwait(false);
        }
        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {
            // Host is stopping; the entry stays staged and replays on restart (at-least-once).
        }
    }

    // Replay success removes the dead-letter entry. If the store cannot record the removal,
    // the entry stays and may be replayed again: at-least-once beats a stopped pump.
    private async Task CompleteReplayAsync(IDeferredEnvelope envelope, CancellationToken stoppingToken) {
        if(envelope is not DeadLetterReplayEnvelope replay) {
            return;
        }

        try {
            await _deadLetters.RemoveAsync(replay.DeadLetterKey, stoppingToken).ConfigureAwait(false);
        }
        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {
            // Entry stays for the next replay.
        }
        catch(Exception) {
            // The command ran; only bookkeeping failed. Keep the pump alive.
        }
    }

    // Moves a failure into the dead-letter store, then completes the staged entry so it cannot
    // run twice. A new failure is recorded as a new entry; a replay failure increments the
    // existing entry. If the dead-letter store is unavailable, the staged entry is left behind
    // for restart recovery instead of dropping the fault.
    private async Task MoveToDeadLetterAsync(DeferredItem item, Exception exception, DateTimeOffset failedAtUtc, CancellationToken stoppingToken) {
        try {
            if(item.Envelope is DeadLetterReplayEnvelope replay) {
                await _deadLetters.RecordFailureAsync(replay.DeadLetterKey, exception, failedAtUtc, stoppingToken).ConfigureAwait(false);
            } else {
                await _deadLetters.RecordAsync(item.Envelope, exception, failedAtUtc, stoppingToken).ConfigureAwait(false);
            }
        }
        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {
            return;
        }
        catch(Exception) {
            return; // Dead-letter store unavailable: keep the staged entry for restart recovery.
        }

        await CompleteAsync(item.Key, stoppingToken).ConfigureAwait(false);
    }

    private sealed class DeferredItem(string Key, IDeferredEnvelope Envelope, CancellationToken Cancellation) {
        public string Key { get; } = Key;
        public IDeferredEnvelope Envelope { get; } = Envelope;
        public CancellationToken Cancellation { get; } = Cancellation;
    }
}
