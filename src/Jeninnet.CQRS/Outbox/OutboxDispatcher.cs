//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Outbox;

/// <summary>
/// Retries pending outbox notifications through the <see cref="IMessageRelay"/> seam: every
/// scope-recovered entry is published at least once, and a failure leaves it pending for the
/// next pass. When <c>AddCqrsOutboxRelay&lt;TTransport&gt;()</c> enables the transport relay,
/// this in-process dispatcher stands down so the relay is the only publisher.
/// </summary>
/// <param name="store">The outbox store. Never <see langword="null"/>.</param>
/// <param name="scopes">Used to open one scope per notification. Never <see langword="null"/>.</param>
/// <param name="relay">Executes each notification envelope. Never <see langword="null"/>.</param>
/// <param name="options">The polling options. Never <see langword="null"/>.</param>
/// <param name="clock">The clock used for the retry interval. Never <see langword="null"/>.</param>
/// <param name="relayOptions">The transport-relay ownership flag. Never <see langword="null"/>.</param>
/// <param name="tenant">The ambient tenant adapter; <see langword="null"/> without tenancy.</param>
public sealed class OutboxDispatcher(
    IOutboxStore store,
    IServiceScopeFactory scopes,
    IMessageRelay relay,
    IOptions<NotificationOutboxOptions> options,
    TimeProvider clock,
    IOptions<OutboxRelayOptions> relayOptions,
    TenantAccessor? tenant = null) : BackgroundService {
    private static readonly TimeSpan _defaultInterval = TimeSpan.FromSeconds(5);

    private readonly IOutboxStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IServiceScopeFactory _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
    private readonly IMessageRelay _relay = relay ?? throw new ArgumentNullException(nameof(relay));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly bool _transportRelay = (relayOptions ?? throw new ArgumentNullException(nameof(relayOptions))).Value.Enabled;
    private readonly TenantAccessor? _tenant = tenant;
    private readonly TimeSpan _interval = (options ?? throw new ArgumentNullException(nameof(options))).Value.PollInterval > TimeSpan.Zero
        ? options.Value.PollInterval
        : _defaultInterval;

    /// <summary>
    /// Publishes every pending notification once. Entries that fail stay pending. Returns the
    /// number delivered. When the transport relay owns the outbox this is a no-op, so the
    /// in-process dispatcher and the relay never publish the same row.
    /// </summary>
    /// <param name="cancellationToken">Stops the pass between entries.</param>
    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken = default) {
        if(_transportRelay) {
            return 0;
        }

        var pending = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        var delivered = 0;

        foreach(var (key, notification) in pending) {
            cancellationToken.ThrowIfCancellationRequested();

            try {
                using var tenantScope = _tenant?.EnterScope(notification.TenantId);
                using var scope = _scopes.CreateScope();
                await _relay.RelayAsync(notification, scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
                await _store.CompleteAsync(key, cancellationToken).ConfigureAwait(false);
                delivered++;
            }
            catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch(Exception) {
                // At-least-once: the entry stays pending and the next pass retries it.
            }
        }

        return delivered;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if(_transportRelay) {
            return;
        }

        while(!stoppingToken.IsCancellationRequested) {
            try {
                await DispatchPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {
                return;
            }
            catch(Exception) {
                // A store failure must not kill the host; the next tick retries.
            }

            try {
                await _clock.DelayAsync(_interval, stoppingToken).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {
                return;
            }
        }
    }
}