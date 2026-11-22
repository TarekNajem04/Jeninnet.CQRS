//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
#if NETSTANDARD2_0 || NET462
namespace System.Threading;

/// <summary>A timer created by <see cref="System.TimeProvider"/>.</summary>
public interface ITimer : IDisposable, IAsyncDisposable {
    /// <summary>Changes the timer schedule.</summary>
    /// <param name="dueTime">When the next callback fires.</param>
    /// <param name="period">The interval between callbacks.</param>
    /// <returns><see langword="true"/> when the change was accepted.</returns>
    bool Change(TimeSpan dueTime, TimeSpan period);
}
#endif
