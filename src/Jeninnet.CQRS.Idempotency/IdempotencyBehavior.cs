//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency;

/// <summary>
/// A pipeline behavior that short-circuits duplicate commands implementing
/// <see cref="IIdempotentCommand{TResponse}"/>, returning the previously stored response instead
/// of invoking the handler again. Concurrent duplicates are serialized by an execution lease:
/// one caller runs the handler, the others wait for (or fail fast on) its stored result.
/// </summary>
/// <typeparam name="TRequest">
///   The command type, which must implement <see cref="IIdempotentCommand{TResponse}"/>.
/// </typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
/// Constructs the behavior with the given <see cref="IIdempotencyStore"/>. Lease policy lives in
/// <see cref="IdempotencyBehaviorOptions"/>; the defaults wait up to 30 seconds, keep leases for
/// 30 seconds, and poll every 50 milliseconds.
/// <para>
/// Lease TTL is renewed by a background heartbeat only when
/// <see cref="IdempotencyBehaviorOptions.HeartbeatInterval"/> is set (the default is no
/// heartbeat). Without a heartbeat, keep <see cref="IdempotencyBehaviorOptions.LeaseTimeToLive"/>
/// above the handler's worst-case duration. A crashed owner is reclaimed by the next caller once
/// its TTL expires; the command guarantees exactly-once execution only while the owner completes
/// inside the lease and the response write succeeds.
/// </para>
/// </remarks>
/// <param name="store">The store used to lease keys and to get/store idempotent command results.</param>
/// <param name="options">Behavior options; defaults are used when omitted.</param>
/// <param name="tenantKeys">Tenant-qualified key composer; <see langword="null"/> when multi-tenancy is not registered.</param>
public sealed class IdempotencyBehavior<TRequest, TResponse>(
    IIdempotencyStore store,
    IOptions<IdempotencyBehaviorOptions>? options = null,
    ITenantScopedKey? tenantKeys = null) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IIdempotentCommand<TResponse> {
    private static readonly string _processIdentity = $"{Environment.MachineName}:{ProcessIdentity.Id}";

    private readonly IIdempotencyStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IdempotencyBehaviorOptions _options = Validate(options?.Value ?? new IdempotencyBehaviorOptions());
    private readonly ITenantScopedKey? _tenantKeys = tenantKeys;

    /// <inheritdoc/>
    public async Task<TResponse> HandleAsync(TRequest request, Func<TRequest, CancellationToken, Task<TResponse>> next, CancellationToken cancellationToken) {
        Guard.NotNull(request);
        Guard.NotNull(next);

        var key = _tenantKeys?.Compose(request.IdempotencyKey) ?? request.IdempotencyKey;
        Guard.NotNullOrWhiteSpace(key);

        var owner = $"{_processIdentity}:{Guid.NewGuid():N}";

        // One wait budget per dispatch, independent of how many poll iterations it takes.
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(_options.WaitTimeout);

        while(true) {
            var lease = await _store.TryAcquireAsync<TResponse>(key, owner, _options.LeaseTimeToLive, cancellationToken)
                                    .ConfigureAwait(false);

            switch(lease.State) {
                case IdempotencyLeaseState.Completed:
                    return lease.Response!;

                case IdempotencyLeaseState.Acquired:
                    return await ExecuteAsync(request, next, key, owner, cancellationToken).ConfigureAwait(false);

                case IdempotencyLeaseState.Busy:
                    if(_options.FailFastWhenBusy) {
                        throw new ConcurrentExecutionException(
                            key,
                            lease.Owner,
                            $"Idempotent command '{key}' is already executing under owner '{lease.Owner}'.");
                    }

                    try {
                        await Task.Delay(_options.PollInterval, wait.Token).ConfigureAwait(false);
                    }
                    catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested) {
                        throw new ConcurrentExecutionException(
                            key,
                            lease.Owner,
                            $"Timed out after {_options.WaitTimeout} waiting for the execution of idempotent command '{key}'.");
                    }

                    break;

                default:
                    throw new InvalidOperationException($"Unknown idempotency lease state '{lease.State}'.");
            }
        }
    }

    private async Task<TResponse> ExecuteAsync(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResponse>> next,
        string key,
        string owner,
        CancellationToken cancellationToken) {
        using var heartbeat = new CancellationTokenSource();
        var heartbeatTask = _options.HeartbeatInterval is { } interval
            ? RenewWhileRunningAsync(key, owner, interval, heartbeat.Token)
            : null;

        try {
            var response = await next(request, cancellationToken).ConfigureAwait(false);

            // The handler's side effects already happened. Do not let caller cancellation lose
            // the response that concurrent waiters need; only the lease owner may store it.
            var completed = await _store.CompleteAsync(key, owner, response, CancellationToken.None).ConfigureAwait(false);

            if(!completed) {
                throw new ConcurrentExecutionException(
                    key,
                    owner,
                    $"The execution lease for idempotent command '{key}' was lost before its response could be stored; the outcome is uncertain.");
            }

            return response;
        }
        catch {
            // A failed handler must not block the key until the TTL expires. Release is
            // best-effort and never masks the original failure.
            await ReleaseQuietlyAsync(key, owner).ConfigureAwait(false);

            throw;
        }
        finally {
            heartbeat.Cancel(); // NOSONAR: The synchronous API is intentional for netstandard2.0/net462 compatibility.

            if(heartbeatTask is not null) {
                try {
                    await heartbeatTask.ConfigureAwait(false);
                }
                catch(OperationCanceledException) {
                    // Normal stop: the handler finished before the next renewal tick.
                }
            }
        }
    }

    private async Task RenewWhileRunningAsync(string key, string owner, TimeSpan interval, CancellationToken cancellationToken) {
        while(!cancellationToken.IsCancellationRequested) {
            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);

            try {
                if(!await _store.RenewAsync(key, owner, _options.LeaseTimeToLive, cancellationToken).ConfigureAwait(false)) {
                    return; // Lease lost or expired; completion will surface the conflict.
                }
            }
            catch(Exception) {
                // A heartbeat failure must not replace the handler's result. The lease will
                // expire and the next caller will reclaim it if the outage persists.
                return;
            }
        }
    }

    private async Task ReleaseQuietlyAsync(string key, string owner) {
        try {
            await _store.ReleaseAsync(key, owner, CancellationToken.None).ConfigureAwait(false);
        }
        catch(Exception) {
            // Preserve the handler's exception; the lease expires on its own if release failed.
        }
    }

    private static IdempotencyBehaviorOptions Validate(IdempotencyBehaviorOptions options) {
        options.Validate();

        return options;
    }
}
