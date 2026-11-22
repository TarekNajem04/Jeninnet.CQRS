//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Resilience;

/// <summary>
/// A pipeline behavior that applies a retry policy with exponential back-off around the handler
/// invocation, retrying on any <see cref="Exception"/>. When the circuit breaker is enabled it
/// additionally guards the handler: after <see cref="RetryBehaviorOptions.CircuitBreakerFailureThreshold"/>
/// consecutive failures the circuit opens and requests fail fast; after
/// <see cref="RetryBehaviorOptions.CircuitBreakerOpenSeconds"/> a single trial request is admitted
/// (half-open) while concurrent requests wait for its outcome.
/// </summary>
/// <typeparam name="TRequest">The request type (command or query).</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
/// The breaker state is shared by all dispatches through this instance: register the behavior
/// as a singleton (see <c>AddCqrsResilienceBehavior</c>) or the breaker can never trip.
/// State transitions use lock-free primitives (<see cref="Volatile"/> reads/writes and
/// <see cref="Interlocked.CompareExchange{T}(ref T, T, T)"/>); the trial gate never blocks a
/// thread — waiters asynchronously await the trial outcome.
/// </remarks>
public sealed class RetryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> {
    private const int CLOSED_STATE = (int)CircuitBreakerState.Closed;
    private const int OPEN_STATE = (int)CircuitBreakerState.Open;
    private const int HALF_OPEN_STATE = (int)CircuitBreakerState.HalfOpen;

    private const string BREAKER_OPEN_MESSAGE = "Circuit breaker is open";

    private readonly RetryBehaviorOptions _options;

    private int _state;
    private int _consecutiveFailures;
    private long _openedAtTicks;
    private TaskCompletionSource<TrialOutcome>? _trialGate;

    /// <summary>
    /// Occurs when the circuit breaker changes state. Subscribers must not block;
    /// a subscriber exception propagates to the dispatching caller.
    /// </summary>
    public event EventHandler<CircuitBreakerStateChangedEventArgs>? BreakerStateChanged;

    /// <summary>
    /// Gets a point-in-time view of the circuit breaker for health checks and diagnostics.
    /// </summary>
    public CircuitBreakerSnapshot BreakerState {
        get {
            var state = (CircuitBreakerState)Volatile.Read(ref _state);
            var failures = Volatile.Read(ref _consecutiveFailures);
            var openedAtTicks = Volatile.Read(ref _openedAtTicks);

            return new CircuitBreakerSnapshot(
                state,
                failures,
                openedAtTicks is 0 ? null : new DateTime(openedAtTicks, DateTimeKind.Utc));
        }
    }

    /// <summary>
    /// Initializes a new instance with the given options and no breaker registry.
    /// </summary>
    /// <param name="options">The retry behavior options.</param>
    public RetryBehavior(IOptions<RetryBehaviorOptions> options)
        : this(options, registry: null) {
    }

    /// <summary>
    /// Initializes a new instance with the given options and an optional breaker registry.
    /// </summary>
    /// <param name="options">The retry behavior options.</param>
    /// <param name="registry">The registry receiving this breaker's snapshots, or <see langword="null"/> for none.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown if <c>options.Value.RetryCount</c> is less than 1.</exception>
    public RetryBehavior(IOptions<RetryBehaviorOptions> options, CircuitBreakerRegistry? registry) {
        Guard.NotNull(options);
        _options = options.Value;

        if(_options.RetryCount < 1) {
            throw new ArgumentException("RetryCount must be ≥ 1", nameof(options));
        }

        registry?.Register(typeof(TRequest).FullName ?? typeof(TRequest).Name, () => BreakerState);
    }

    /// <summary>
    /// Runs the next pipeline stage with retries, exponential back-off, and the optional
    /// circuit breaker.
    /// </summary>
    /// <param name="request">The request instance.</param>
    /// <param name="next">The next stage.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    public async Task<TResponse> HandleAsync(TRequest request, Func<TRequest, CancellationToken, Task<TResponse>> next, CancellationToken cancellationToken) { // NOSONAR: Complexity is inherent to this state machine/pipeline and is covered by tests.
        if(!_options.EnableCircuitBreaker) {
            return await ExecuteClosedAsync(request, next, cancellationToken).ConfigureAwait(false);
        }

        while(true) {
            var state = Volatile.Read(ref _state);

            if(state is CLOSED_STATE) {
                return await ExecuteClosedAsync(request, next, cancellationToken).ConfigureAwait(false);
            }

            if(state is OPEN_STATE) {
                if(!CooldownElapsed()) {
                    throw new InvalidOperationException(BREAKER_OPEN_MESSAGE);
                }

                // Cooldown elapsed: exactly one caller wins the trial; the rest re-evaluate
                // and end up waiting on the trial gate below.
                if(Interlocked.CompareExchange(ref _state, HALF_OPEN_STATE, OPEN_STATE) is OPEN_STATE) {
                    BeginTrial();
                    return await ExecuteTrialAsync(request, next, cancellationToken).ConfigureAwait(false);
                }

                continue;
            }

            // HalfOpen: wait for the in-flight trial, then re-decide on its outcome.
            var gate = Volatile.Read(ref _trialGate);

            if(gate is null) {
                // Unreachable when the trial publishes the gate before opening half-open;
                // yield instead of spinning in case a torn read ever occurs.
                await Task.Yield();
                continue;
            }

            var outcome = await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            if(outcome is TrialOutcome.Succeeded) {
                continue; // Closed again: run the normal closed path.
            }

            // A failed (or cancelled — conservatively re-opened) trial fails the waiter fast.
            throw new InvalidOperationException(BREAKER_OPEN_MESSAGE);
        }
    }

    private async Task<TResponse> ExecuteClosedAsync(TRequest request, Func<TRequest, CancellationToken, Task<TResponse>> next, CancellationToken cancellationToken) {
        for(var attempt = 1; attempt <= _options.RetryCount; attempt++) {
            try {
                var result = await ExecuteWithTimeoutAsync(request, next, cancellationToken).ConfigureAwait(false);
                ResetOnSuccess();
                return result;
            }
            catch(Exception) {
                RegisterFailure();

                if(attempt < _options.RetryCount) {
                    var delay = (int)(_options.BaseDelayMilliseconds * Math.Pow(2, attempt - 1));
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                } else {
                    throw;
                }
            }
        }

        throw new InvalidOperationException("Unexpected retry loop exit");
    }

    private async Task<TResponse> ExecuteTrialAsync(TRequest request, Func<TRequest, CancellationToken, Task<TResponse>> next, CancellationToken cancellationToken) {
        // A trial is a single probe attempt: no retry loop, so one downstream call decides.
        try {
            var result = await ExecuteWithTimeoutAsync(request, next, cancellationToken).ConfigureAwait(false);
            CloseFromTrial();
            return result;
        }
        catch(OperationCanceledException) {
            // Cancellation is not a downstream failure, but the half-open state cannot be left
            // dangling: conservatively re-open so the next probe follows a fresh cooldown.
            // Waiters observe the failure outcome and fail fast instead of hanging.
            ReopenFromTrial();
            throw;
        }
        catch(Exception) {
            ReopenFromTrial();
            throw;
        }
    }

    private async Task<TResponse> ExecuteWithTimeoutAsync(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResponse>> next,
        CancellationToken cancellationToken) {
        if(_options.TimeoutSeconds > 0) {
            using var timeoutCts = new CancellationTokenSource(
                TimeSpan.FromSeconds(_options.TimeoutSeconds));

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            var task = next(request, linkedCts.Token);
            var completed = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, linkedCts.Token)).ConfigureAwait(false);

            if(completed != task) {
                throw new TimeoutException($"Request timed out after {_options.TimeoutSeconds} seconds");
            }

            return await task.ConfigureAwait(false);
        } else {
            return await next(request, cancellationToken).ConfigureAwait(false);
        }
    }

    private bool CooldownElapsed() {
        var openedAtTicks = Volatile.Read(ref _openedAtTicks);
        var openIntervalTicks = (long)_options.CircuitBreakerOpenSeconds * TimeSpan.TicksPerSecond;

        return DateTime.UtcNow.Ticks - openedAtTicks >= openIntervalTicks;
    }

    private void BeginTrial() {
        // Runs synchronously on the CAS winner with no awaits in between, so the gate below
        // is published before any waiter can observe the HalfOpen state for longer than a
        // thread-scheduling quantum. Waiters that still observe a null gate yield and re-read.
        Volatile.Write(ref _trialGate, new TaskCompletionSource<TrialOutcome>(TaskCreationOptions.RunContinuationsAsynchronously));
        FireStateChanged(CircuitBreakerState.Open, CircuitBreakerState.HalfOpen);
    }

    private void CloseFromTrial() {
        Volatile.Write(ref _consecutiveFailures, 0);
        Volatile.Write(ref _state, CLOSED_STATE);
        FireStateChanged(CircuitBreakerState.HalfOpen, CircuitBreakerState.Closed);
        CompleteTrial(TrialOutcome.Succeeded);
    }

    private void ReopenFromTrial() {
        Volatile.Write(ref _openedAtTicks, DateTime.UtcNow.Ticks);
        Volatile.Write(ref _consecutiveFailures, _options.CircuitBreakerFailureThreshold);
        Volatile.Write(ref _state, OPEN_STATE);
        FireStateChanged(CircuitBreakerState.HalfOpen, CircuitBreakerState.Open);
        CompleteTrial(TrialOutcome.Failed);
    }

    private void CompleteTrial(TrialOutcome outcome) {
        var gate = Volatile.Read(ref _trialGate);
        gate?.TrySetResult(outcome);
    }

    private void RegisterFailure() {
        if(!_options.EnableCircuitBreaker) {
            return;
        }

        var failures = Interlocked.Increment(ref _consecutiveFailures);

        if(failures < _options.CircuitBreakerFailureThreshold) {
            return;
        }

        Volatile.Write(ref _openedAtTicks, DateTime.UtcNow.Ticks);

        if(Interlocked.CompareExchange(ref _state, OPEN_STATE, CLOSED_STATE) is CLOSED_STATE) {
            FireStateChanged(CircuitBreakerState.Closed, CircuitBreakerState.Open);
        }
    }

    private void ResetOnSuccess() {
        if(!_options.EnableCircuitBreaker) {
            return;
        }

        Volatile.Write(ref _consecutiveFailures, 0);

        if(Volatile.Read(ref _state) is OPEN_STATE
            && Interlocked.CompareExchange(ref _state, CLOSED_STATE, OPEN_STATE) is OPEN_STATE) {
            Volatile.Write(ref _openedAtTicks, 0);
            FireStateChanged(CircuitBreakerState.Open, CircuitBreakerState.Closed);
        }
    }

    private void FireStateChanged(CircuitBreakerState oldState, CircuitBreakerState newState) =>
        BreakerStateChanged?.Invoke(this, new CircuitBreakerStateChangedEventArgs(oldState, newState));

    private enum TrialOutcome {
        Succeeded,
        Failed
    }
}
