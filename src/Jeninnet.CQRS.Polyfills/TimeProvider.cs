//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
#if NETSTANDARD2_0 || NET462
namespace System;

/// <summary>
/// netstandard2.0 implementation of the .NET 8 clock abstraction. It is public because the
/// existing public API exposes <see cref="TimeProvider"/>; modern target frameworks use the
/// framework type instead of this shim.
/// </summary>
public abstract class TimeProvider {
    private static readonly TimeProvider _system = new SystemTimeProvider();

    /// <summary>Gets a provider backed by the system clock.</summary>
    public static TimeProvider System => _system;

    /// <summary>Initializes the provider.</summary>
    protected TimeProvider() {
    }

    /// <summary>Gets the current UTC time.</summary>
    public virtual DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;

    /// <summary>Gets the current time in <see cref="LocalTimeZone"/>.</summary>
    public DateTimeOffset GetLocalNow() {
        var utc = GetUtcNow();

        return TimeZoneInfo.ConvertTime(utc, LocalTimeZone);
    }

    /// <summary>Gets the local time zone.</summary>
    public virtual TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;

    /// <summary>Gets the frequency of <see cref="GetTimestamp"/>.</summary>
    public virtual long TimestampFrequency => Diagnostics.Stopwatch.Frequency;

    /// <summary>Gets a monotonic timestamp.</summary>
    public virtual long GetTimestamp() => Diagnostics.Stopwatch.GetTimestamp();

    /// <summary>Creates a timer that invokes <paramref name="callback"/>.</summary>
    /// <param name="callback">The callback to invoke.</param>
    /// <param name="state">Optional state passed to the callback.</param>
    /// <param name="dueTime">When the first callback fires; <see cref="Threading.Timeout.InfiniteTimeSpan"/> disables it.</param>
    /// <param name="period">The interval between callbacks; <see cref="Threading.Timeout.InfiniteTimeSpan"/> makes it one-shot.</param>
    public virtual Threading.ITimer CreateTimer(Threading.TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) {
        if(callback is null) {
            throw new ArgumentNullException(nameof(callback));
        }

        return new TimerShim(callback, state, dueTime, period);
    }

    /// <summary>Gets the elapsed time since <paramref name="startingTimestamp"/>.</summary>
    /// <param name="startingTimestamp">A value returned by <see cref="GetTimestamp"/>.</param>
    public TimeSpan GetElapsedTime(long startingTimestamp) => GetElapsedTime(startingTimestamp, GetTimestamp());

    /// <summary>Gets the elapsed time between two timestamps.</summary>
    /// <param name="startingTimestamp">The earlier timestamp.</param>
    /// <param name="endingTimestamp">The later timestamp.</param>
    public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp) =>
        TimeSpan.FromSeconds((endingTimestamp - startingTimestamp) / (double)TimestampFrequency);

    private sealed class SystemTimeProvider : TimeProvider {
    }

    private sealed class TimerShim : Threading.ITimer {
        private readonly Threading.Timer _timer;

        internal TimerShim(Threading.TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) {
            _timer = new Threading.Timer(callback, state, dueTime, period);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period) => _timer.Change(dueTime, period);

        public void Dispose() => _timer.Dispose();

        public ValueTask DisposeAsync() {
            _timer.Dispose();

            return default;
        }
    }
}
#endif
