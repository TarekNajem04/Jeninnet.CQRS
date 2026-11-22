//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//

namespace Jeninnet.CQRS.Inbox;

/// <summary>
/// Wraps every inbound delivery in the inbox table: the first delivery of a message id runs the
/// handler, a redelivery with the same id is dropped before the handler. Registered by
/// <c>AddCqrsInbox</c> as a receive middleware, so it observes handler completion.
/// </summary>
/// <remarks>
/// The pending row is abandoned when the handler throws, allowing an immediate retry. If the
/// process crashes after the handler succeeds but before <see cref="IInboxStore.CompleteAsync"/>,
/// the row expires after <see cref="InboxOptions.PendingTimeout"/> and the broker redelivery runs
/// again — the documented at-least-once window. Set <see cref="InboxOptions.AutoComplete"/> to
/// <see langword="false"/> and complete the row inside the handler's database transaction to
/// close that window.
/// </remarks>
internal sealed class InboxReceiveMiddleware(
    IInboxStore store,
    IOptions<InboxOptions> options,
    InboxMetrics metrics,
    TimeProvider? clock = null,
    ITenantContext? tenant = null) : IReceiveMiddleware {
    private readonly IInboxStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly InboxOptions _options = Validate(options);
    private readonly InboxMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ITenantContext? _tenant = tenant;

    /// <inheritdoc />
    public async Task<bool> InvokeAsync(
        ReceiveMiddlewareContext context,
        Func<CancellationToken, Task<bool>> next,
        CancellationToken cancellationToken = default) {
        Guard.NotNull(context);
        Guard.NotNull(next);

        if(!_options.Enabled || StringCompat.IsNullOrWhiteSpace(context.Envelope.MessageId)) {
            return await next(cancellationToken).ConfigureAwait(false);
        }

        var now = _clock.GetUtcNow();
        var message = new InboxMessage(
            context.Envelope.MessageId,
            now,
            now + _options.PendingTimeout,
            ResolveTenant(context.Envelope.TenantId));

        if(!await _store.TryBeginAsync(message, cancellationToken).ConfigureAwait(false)) {
            _metrics.RecordDuplicate(context.Envelope.MessageType);

            return false;
        }

        try {
            var handled = await next(cancellationToken).ConfigureAwait(false);

            if(!handled) {
                await _store.AbandonAsync(message.MessageId, message.TenantId, cancellationToken).ConfigureAwait(false);

                return false;
            }

            if(_options.AutoComplete) {
                var hash = _options.ComputePayloadHash ? ComputeHash(context.Envelope) : null;
                await _store
                    .CompleteAsync(message.MessageId, message.TenantId, hash, _clock.GetUtcNow(), cancellationToken)
                    .ConfigureAwait(false);
                _metrics.RecordProcessed(context.Envelope.MessageType);
            }

            return true;
        }
        catch {
            try {
                await _store.AbandonAsync(message.MessageId, message.TenantId, CancellationToken.None).ConfigureAwait(false);
            }
            catch {
                // Preserve the handler failure; a pending row expires after PendingTimeout.
            }

            throw;
        }
    }

    private static InboxOptions Validate(IOptions<InboxOptions>? options) {
        var value = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        value.Validate();

        return value;
    }

    private string? ResolveTenant(string? tenantId) {
        var current = _tenant?.Current;

        return StringCompat.IsNullOrWhiteSpace(current) ? tenantId : current;
    }

    private static string? ComputeHash(TransportEnvelope envelope) {
        if(envelope.Payload.IsEmpty) {
            return null;
        }

#if NET6_0_OR_GREATER
        var hash = SHA256.HashData(envelope.Payload.Span);
#else
        using var algorithm = SHA256.Create();
        var hash = algorithm.ComputeHash(envelope.Payload.ToArray());
#endif
        var builder = new StringBuilder(hash.Length * 2);

        foreach(var value in hash) {
            builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}