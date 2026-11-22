//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Jobs;

/// <summary>
/// Prunes terminal jobs past the retention window. A store outage never stops the host; the next
/// tick retries.
/// </summary>
internal sealed class JobPruningService(
    IJobStore store,
    IOptions<JobOptions> options,
    TimeProvider clock) : BackgroundService {
    private readonly IJobStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly JobOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        _options.Validate();

        if(_options.PruneInterval <= TimeSpan.Zero) {
            return;
        }

        while(!stoppingToken.IsCancellationRequested) {
            try {
                await _store
                    .PruneAsync(_clock.GetUtcNow() - _options.Retention, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {
                return;
            }
            catch {
                // The next prune tick retries; a store outage must not stop the host.
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

/// <summary>Marker that keeps the worker hosted registration idempotent without a second instance.</summary>
internal sealed class JobWorkerHostedMarker; // NOSONAR: Intentional marker type used for idempotent DI registration.
