//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Outbox;

/// <summary>
/// One leased outbox row. The owner must complete or fail the lease before it expires; an expired
/// lease is reclaimable by another worker, which is what makes relay delivery at-least-once.
/// </summary>
/// <param name="Key">The store-assigned row key.</param>
/// <param name="Envelope">The rebuilt notification envelope.</param>
/// <param name="Attempts">How many delivery attempts have failed so far.</param>
/// <param name="LeaseExpiresAtUtc">When the current owner's lease expires.</param>
public sealed record OutboxLease(
    string Key,
    IDeferredEnvelope Envelope,
    int Attempts,
    DateTimeOffset LeaseExpiresAtUtc);
