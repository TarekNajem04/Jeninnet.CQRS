//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>
/// Store-specific options for <see cref="EfInboxStore"/>.
/// </summary>
public sealed class EfInboxStoreOptions {
    /// <summary>
    /// Gets or sets how many times a store retries after losing an optimistic-concurrency race
    /// before surfacing the error. Defaults to 10.
    /// </summary>
    public int MaxAttempts { get; set; } = 10;
}