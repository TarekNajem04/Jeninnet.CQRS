//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Resilience;

/// <summary>
/// The exception thrown when the rate limiter refuses a request: no permit available and the
/// queue is full (or queuing is disabled).
/// </summary>
/// <param name="key">The rate-limiter partition key that ran out of permits.</param>
public sealed class RateLimitRejectedException(string key)
    : InvalidOperationException($"Request rejected by the rate limiter (key '{key}').") {
    // RCS1194 (implement the full BCL constructor set) is deliberately not applied:
    // every instance of this exception carries the refusing partition key, so a
    // parameterless or message-only constructor could not establish the invariant.
    // A serialization constructor is omitted on purpose (SYSLIB0051: BinaryFormatter
    // is obsolete); this exception never crosses an AppDomain boundary.
#pragma warning disable RCS1194 // Justification: key is fundamental; serialization ctor omitted per SYSLIB0051.
#pragma warning restore RCS1194

    /// <summary>Gets the rate-limiter partition key that ran out of permits.</summary>
    public string Key { get; } = key;
}