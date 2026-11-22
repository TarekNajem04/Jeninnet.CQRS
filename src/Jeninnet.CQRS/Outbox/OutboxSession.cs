//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Outbox;

/// <summary>
/// Per-scope outbox transaction participant: queues notifications while a transaction is in
/// flight, discards them on rollback, and on commit stages each one durably then publishes it.
/// With the transport relay enabled it stages only; <see cref="OutboxRelayWorker{TTransport}"/>
/// publishes through the broker.
/// </summary>
/// <remarks>
/// Publishing after commit is deliberate: writing the outbox row first would make a
/// rolled-back notification restart-replayable. The durability window therefore begins at
/// commit; a crash between commit and the durable stage loses that notification. Once staged,
/// delivery is at-least-once (handlers must be idempotent).
/// </remarks>
/// <param name="store">The outbox store. Never <see langword="null"/>.</param>
/// <param name="provider">The scoped provider used to execute envelopes. Never <see langword="null"/>.</param>
/// <param name="relayOptions">The transport-relay ownership flag. Never <see langword="null"/>.</param>
public sealed class OutboxSession(IOutboxStore store, IServiceProvider provider, IOptions<OutboxRelayOptions> relayOptions) : ITransactionParticipant {
    private readonly IOutboxStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IServiceProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    private readonly bool _transportRelay = (relayOptions ?? throw new ArgumentNullException(nameof(relayOptions))).Value.Enabled;
    private readonly Lock _sync = new();
    private readonly List<IDeferredEnvelope> _pending = [];
    private bool _active;

    /// <summary>
    /// Queues <paramref name="notification"/> when a transaction is active, otherwise stages and
    /// publishes it immediately.
    /// </summary>
    /// <param name="notification">The notification envelope.</param>
    /// <param name="cancellationToken">Cancels the write and publish.</param>
    public Task StageAsync(IDeferredEnvelope notification, CancellationToken cancellationToken) {
        Guard.NotNull(notification);

        lock(_sync) {
            if(_active) {
                _pending.Add(notification);
                return Task.CompletedTask;
            }
        }

        return StoreAndPublishAsync(notification, cancellationToken);
    }

    /// <inheritdoc />
    public Task BeginAsync(CancellationToken cancellationToken) {
        lock(_sync) {
            _pending.Clear();
            _active = true;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken cancellationToken) {
        IDeferredEnvelope[] pending;
        lock(_sync) {
            pending = [.. _pending];
            _pending.Clear();
            _active = false;
        }

        foreach(var notification in pending) {
            await StoreAndPublishAsync(notification, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task RollbackAsync(CancellationToken cancellationToken) {
        lock(_sync) {
            _pending.Clear();
            _active = false;
        }

        return Task.CompletedTask;
    }

    // Stage first, then publish: a crash after staging is restart-replayable. A publish failure
    // leaves the entry pending for the dispatcher; the caller is not failed because the
    // transaction itself committed. With the transport relay enabled, staging is the whole job:
    // the worker polls and publishes through the broker.
    private async Task StoreAndPublishAsync(IDeferredEnvelope notification, CancellationToken cancellationToken) {
        var key = await _store.StageAsync(notification, cancellationToken).ConfigureAwait(false);

        if(_transportRelay) {
            return;
        }

        try {
            await notification.DispatchAsync(_provider, cancellationToken).ConfigureAwait(false);
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch(Exception) {
            return;
        }

        await _store.CompleteAsync(key, cancellationToken).ConfigureAwait(false);
    }
}