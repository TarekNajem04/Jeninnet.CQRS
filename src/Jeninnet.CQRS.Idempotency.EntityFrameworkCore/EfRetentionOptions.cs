//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>Retention switches owned by the EF Core package.</summary>
public sealed class EfRetentionOptions {
    /// <summary>
    /// Gets or sets whether a completed outbox row is kept with a completion timestamp until the
    /// retention sweeper archives it. Defaults to <see langword="false"/>, preserving the
    /// historical delete-on-completion behavior. <c>AddCqrsEfRetention</c> enables it.
    /// </summary>
    public bool ArchiveCompletedOutbox { get; set; }
}