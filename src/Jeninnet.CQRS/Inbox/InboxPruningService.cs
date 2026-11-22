//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Inbox;

/// <summary>
/// Prunes completed inbox rows past the retention window and abandoned pending rows past the
/// pending timeout. A store outage never stops the host; the next tick retries.
/// </summary>
internal sealed class InboxPruningService(
    IInboxStore store,
    IOptions<InboxOptions> options,
    TimeProvider clock,
    InboxMetrics metrics) : BackgroundService {
    private readonly IInboxStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly InboxOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly InboxMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        _options.Validate();

        if(!_options.Enabled || _options.PruneInterval <= TimeSpan.Zero) {
            return;
        }

        while(!stoppingToken.IsCancellationRequested) {
            try {
                var now = _clock.GetUtcNow();
                var pendingBefore = _options.PendingTimeout > TimeSpan.Zero
                    ? now - _options.PendingTimeout
                    : now;
                var pruned = await _store
                    .PruneAsync(now - _options.Retention, pendingBefore, stoppingToken)
                    .ConfigureAwait(false);
                _metrics.RecordPruned(pruned);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {
                return;
            }
            catch {
                // The next prune tick retries; an inbox outage must not stop the host.
            }

            try {
                await Task.Delay(_options.PruneInterval, stoppingToken).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {
                return;
            }
        }
    }
}