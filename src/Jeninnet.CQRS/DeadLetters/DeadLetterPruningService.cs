//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.DeadLetters;

/// <summary>
/// Hosted retention: periodically removes dead letters older than
/// <see cref="DeadLetterOptions.Retention"/> and trims the store to
/// <see cref="DeadLetterOptions.MaxEntries"/>.
/// </summary>
/// <remarks>
/// Uses <see cref="TimeProvider"/> for both the delay and the cutoff, so fake clocks in tests
/// drive it deterministically. Disabled when
/// <see cref="DeadLetterOptions.EnablePruning"/> is <see langword="false"/> or
/// <see cref="DeadLetterOptions.PruneInterval"/> is non-positive.
/// </remarks>
/// <param name="store">The dead-letter store to prune. Never <see langword="null"/>.</param>
/// <param name="options">The retention options. Never <see langword="null"/>.</param>
/// <param name="clock">The clock used for the interval and the cutoff. Never <see langword="null"/>.</param>
public sealed class DeadLetterPruningService(
    IDeadLetterStore store,
    IOptions<DeadLetterOptions> options,
    TimeProvider clock) : BackgroundService {
    private readonly IDeadLetterStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly DeadLetterOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if(!_options.EnablePruning || _options.PruneInterval <= TimeSpan.Zero) {
            return;
        }

        while(!stoppingToken.IsCancellationRequested) {
            try {
                await _clock.DelayAsync(_options.PruneInterval, stoppingToken).ConfigureAwait(false);
                await _store.PruneAsync(_clock.GetUtcNow(), stoppingToken).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {
                return;
            }
        }
    }
}
