//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Outbox;

/// <summary>The outcome of one relay poll, useful for tests and operational logging.</summary>
/// <param name="Leased">Rows claimed by this worker.</param>
/// <param name="Published">Rows handed to the transport successfully.</param>
/// <param name="Retried">Rows left pending with backoff after a failed publish.</param>
/// <param name="DeadLettered">Rows that reached the poison threshold.</param>
public readonly record struct OutboxRelayBatchResult(int Leased, int Published, int Retried, int DeadLettered);