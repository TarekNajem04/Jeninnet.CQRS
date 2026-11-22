//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Caching;

/// <summary>
/// Reports cache failures without ever failing the request: a broken cache means the handler
/// runs, and warnings are throttled to one per <see cref="DistributedCacheOptions.FailureWindow"/>.
/// </summary>
public sealed class CacheFailureReporter {
    private static readonly Action<ILogger, Exception?> _cacheFailure = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(1, "DistributedCacheFailure"),
        "Distributed cache operation failed; falling through to the handler.");

    private readonly Lock _sync = new();
    private readonly TimeProvider _clock;
    private readonly TimeSpan _window;
    private readonly ILogger<CacheFailureReporter> _logger;
    private DateTimeOffset _lastReported = DateTimeOffset.MinValue;

    /// <summary>
    /// Initializes the reporter.
    /// </summary>
    /// <param name="options">The failure-window options. Never <see langword="null"/>.</param>
    /// <param name="clock">The clock used to throttle warnings. Never <see langword="null"/>.</param>
    /// <param name="logger">Optional logger; a null logger is used when logging is not configured.</param>
    public CacheFailureReporter(IOptions<DistributedCacheOptions> options, TimeProvider clock, ILogger<CacheFailureReporter>? logger = null) {
        Guard.NotNull(options);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? NullLogger<CacheFailureReporter>.Instance;
        _window = options.Value.FailureWindow;
    }

    /// <summary>
    /// Logs <paramref name="exception"/> as a warning at most once per failure window.
    /// </summary>
    /// <param name="exception">The cache failure; never surfaces to the caller.</param>
    public void Report(Exception exception) {
        Guard.NotNull(exception);

        var now = _clock.GetUtcNow();

        lock(_sync) {
            if(now - _lastReported < _window) {
                return;
            }

            _lastReported = now;
        }

        _cacheFailure(_logger, exception);
    }
}