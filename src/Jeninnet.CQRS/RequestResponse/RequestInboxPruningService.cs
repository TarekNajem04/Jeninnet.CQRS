//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.RequestResponse;

/// <summary>Prunes expired pending-request rows left behind by crashed clients.</summary>
internal sealed class RequestInboxPruningService(
    IRequestInbox inbox,
    IOptions<RequestResponseOptions> options,
    TimeProvider clock) : BackgroundService {
    private readonly IRequestInbox _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
    private readonly TimeSpan _interval = (options ?? throw new ArgumentNullException(nameof(options))).Value.InboxPruneInterval;
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if(_interval <= TimeSpan.Zero) {
            return;
        }

        while(!stoppingToken.IsCancellationRequested) {
            try {
                await _inbox.RemoveExpiredAsync(_clock.GetUtcNow(), stoppingToken).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {
                return;
            }
            catch(Exception) {
                // The next prune tick retries; a store outage must not stop the host.
            }

            try {
                await Task.Delay(_interval, stoppingToken).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {
                return;
            }
        }
    }
}

/// <summary>Marker that keeps the transport-handler registration idempotent.</summary>
internal sealed class RequestResponseHandlerMarker; // NOSONAR: Intentional marker type used for idempotent DI registration.

/// <summary>Marker for the request-inbox pruning hosted service.</summary>
internal sealed class RequestInboxPruningMarker; // NOSONAR: Intentional marker type used for idempotent DI registration.