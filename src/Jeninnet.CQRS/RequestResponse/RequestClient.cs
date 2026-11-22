//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.RequestResponse;

/// <summary>
/// Default request client. The pending row is registered before publish and removed in every
/// exit path, so timeouts never leave an orphan. First-response clients complete on the first
/// correlated reply; all-response clients collect replies until the timeout expires.
/// </summary>
/// <typeparam name="TRequest">The request message type.</typeparam>
/// <typeparam name="TResponse">The response message type.</typeparam>
internal sealed class RequestClient<TRequest, TResponse>(
    IMessageTransport transport,
    IRequestInbox inbox,
    RequestWaiterRegistry waiters,
    IOptions<RequestResponseOptions> options,
    TimeProvider clock,
    TenantAccessor? tenant = null) : IRequestClient<TRequest, TResponse> {
    private readonly IMessageTransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    private readonly IRequestInbox _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
    private readonly RequestWaiterRegistry _waiters = waiters ?? throw new ArgumentNullException(nameof(waiters));
    private readonly RequestResponseOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly TenantAccessor? _tenant = tenant;

    /// <inheritdoc />
    public Task<TResponse> GetResponseAsync(TRequest request, CancellationToken cancellationToken = default) =>
        GetResponseAsync(request, _options.DefaultTimeout, cancellationToken);

    /// <inheritdoc />
    public async Task<TResponse> GetResponseAsync(TRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) {
        ValidateTimeout(timeout);

        var waiter = new FirstRequestWaiter(typeof(TResponse));
        var requestId = await BeginAsync(request, waiter, timeout, cancellationToken).ConfigureAwait(false);

        try {
            using var timeoutSource = new CancellationTokenSource();
            using var timeoutTimer = _clock.CreateTimer(
                _ => timeoutSource.Cancel(),
                state: null,
                timeout,
                Timeout.InfiniteTimeSpan);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

            try {
                return (TResponse)await waiter.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested) {
                throw new RequestTimeoutException(requestId, typeof(TRequest), typeof(TResponse), timeout);
            }
        }
        finally {
            _waiters.Remove(requestId);
            await CompleteAsync(requestId).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TResponse>> GetAllResponsesAsync(TRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) {
        ValidateTimeout(timeout);

        var waiter = new AllRequestWaiter(typeof(TResponse));
        var requestId = await BeginAsync(request, waiter, timeout, cancellationToken).ConfigureAwait(false);

        try {
            try {
                await _clock.DelayAsync(timeout, cancellationToken).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
                throw;
            }

            var responses = waiter.Snapshot();

            if(responses.Count is 0) {
                throw new RequestTimeoutException(requestId, typeof(TRequest), typeof(TResponse), timeout);
            }

            return [.. responses.Cast<TResponse>()];
        }
        finally {
            _waiters.Remove(requestId);
            await CompleteAsync(requestId).ConfigureAwait(false);
        }
    }

    private async Task<string> BeginAsync(TRequest request, RequestWaiter waiter, TimeSpan timeout, CancellationToken cancellationToken) {
        Guard.NotNull(request);

        var requestId = Guid.NewGuid().ToString("N");
        var now = _clock.GetUtcNow();
        var tenantId = _tenant?.Current;
        _waiters.Add(requestId, waiter);

        try {
            await _inbox.AddAsync(new RequestInboxEntry(
                requestId,
                typeof(TRequest),
                typeof(TResponse),
                waiter.Mode,
                now,
                now + timeout,
                tenantId), cancellationToken).ConfigureAwait(false);

            var headers = new Dictionary<string, string>(StringComparer.Ordinal) {
                [RequestResponseHeaders.Kind] = RequestResponseHeaders.Request,
                [RequestResponseHeaders.RequestId] = requestId,
            };

            await _transport.PublishAsync(
                request,
                new TransportMessageOptions {
                    CorrelationId = requestId,
                    TenantId = tenantId,
                    Headers = headers,
                },
                cancellationToken).ConfigureAwait(false);

            return requestId;
        }
        catch {
            _waiters.Remove(requestId);
            await CompleteAsync(requestId).ConfigureAwait(false);
            throw;
        }
    }

    private async Task CompleteAsync(string requestId) {
        try {
            await _inbox.RemoveAsync(requestId, CancellationToken.None).ConfigureAwait(false);
        }
        catch {
            // A cleanup failure must not mask the original request result; TTL pruning catches it.
        }
    }

    private static void ValidateTimeout(TimeSpan timeout) {
        if(timeout <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be greater than zero.");
        }
    }
}
