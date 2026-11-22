//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Outbox;

/// <summary>
/// Generates outbox keys that sort in staging order. The key starts with the wall-clock tick
/// count and a process-local monotonic sequence, then a random suffix for uniqueness; the store
/// orders by the row's staged time and key, so notifications staged in the same tick still keep
/// their submission order. Keys are opaque to callers.
/// </summary>
public static class OutboxKey {
    private static long _sequence;

    /// <summary>Creates a new sortable, unique outbox key using the system clock.</summary>
    public static string New() => New(DateTimeOffset.UtcNow);

    /// <summary>Creates a new sortable, unique outbox key using <paramref name="now"/>.</summary>
    /// <param name="now">The staging time used as the sortable prefix.</param>
    public static string New(DateTimeOffset now) {
        var timestamp = now.UtcTicks;
        var sequence = Interlocked.Increment(ref _sequence);

        return $"{timestamp:D19}-{sequence:D19}-{Guid.NewGuid():N}";
    }
}