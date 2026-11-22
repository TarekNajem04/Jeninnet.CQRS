//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas;

/// <summary>
/// Options for the saga executor.
/// </summary>
public sealed class SagaOptions {
    /// <summary>
    /// Gets or sets how many times a version conflict is retried before it surfaces. Defaults to
    /// 10, mirroring the idempotency store. Each retry reloads the row and re-evaluates the
    /// transition, so effects are never applied to a stale in-memory copy.
    /// </summary>
    public int MaxConcurrencyRetries { get; set; } = 10;
}
