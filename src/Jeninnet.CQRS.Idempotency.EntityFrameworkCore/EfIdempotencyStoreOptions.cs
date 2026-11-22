//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>
/// Options for <see cref="EfIdempotencyStore"/>.
/// </summary>
public sealed class EfIdempotencyStoreOptions {
    /// <summary>
    /// Gets or sets how long entries live after <c>StoredAtUtc</c>.
    /// Defaults to 30 days; set to <see langword="null"/> for no expiry.
    /// </summary>
    public TimeSpan? TimeToLive { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Gets or sets how many times a store retries after losing an optimistic-concurrency
    /// race before surfacing the error. Defaults to 10; concurrent duplicates converge
    /// in one or two attempts in practice.
    /// </summary>
    public int MaxAttempts { get; set; } = 10;
}
