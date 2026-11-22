//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Resilience;

/// <summary>
/// The exception thrown when a partition is busy and the caller chose not to wait:
/// <see cref="PartitionedConcurrencyOptions.FailFastWhenBusy"/> is enabled or
/// <see cref="PartitionedConcurrencyOptions.WaitTimeout"/> elapsed.
/// </summary>
/// <param name="key">The partition key that is already at its concurrency limit.</param>
public sealed class PartitionBusyException(string key)
    : InvalidOperationException($"Partition '{key}' is busy: its concurrency limit is already reached.") {
    // RCS1194 (implement the full BCL constructor set) is deliberately not applied:
    // every instance of this exception carries the busy partition key, so a parameterless
    // or message-only constructor could not establish the invariant. A serialization
    // constructor is omitted on purpose (SYSLIB0051: BinaryFormatter is obsolete); this
    // exception never crosses an AppDomain boundary.
#pragma warning disable RCS1194 // Justification: key is fundamental; serialization ctor omitted per SYSLIB0051.
#pragma warning restore RCS1194

    /// <summary>Gets the partition key that refused the dispatch.</summary>
    public string Key { get; } = key;
}