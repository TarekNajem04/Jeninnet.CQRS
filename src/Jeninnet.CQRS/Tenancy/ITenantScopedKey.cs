//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tenancy;

/// <summary>
/// Composes a tenant prefix onto a cache, idempotency, rate-limit, or concurrency key. One helper
/// keeps every behavior's partition scheme identical.
/// </summary>
public interface ITenantScopedKey {
    /// <summary>
    /// Returns the tenant-qualified key, or the key unchanged when optional tenancy is enabled and
    /// no tenant is active.
    /// </summary>
    /// <param name="key">The underlying key. Never <see langword="null"/>.</param>
    /// <returns>The tenant-qualified key.</returns>
    string Compose(string key);
}
