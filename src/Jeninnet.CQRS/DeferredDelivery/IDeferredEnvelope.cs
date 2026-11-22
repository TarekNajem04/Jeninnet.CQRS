//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.DeferredDelivery;

/// <summary>
/// A deferred command plus its due time, ready to dispatch through a scope.
/// Implementations close over the concrete command type, so the pump and the
/// relay stay non-generic and trim-safe.
/// </summary>
public interface IDeferredEnvelope {
    /// <summary>Gets the command instance. Never <see langword="null"/>.</summary>
    object Command { get; }

    /// <summary>Gets the message schema version written to durable rows. Defaults to 1.</summary>
    int Version { get; }

    /// <summary>Gets when the command becomes due, in UTC.</summary>
    DateTimeOffset DueUtc { get; }

    /// <summary>Gets when the entry expires, in UTC. <see langword="null"/> never expires.</summary>
    DateTimeOffset? ExpiresAtUtc { get; }

    /// <summary>
    /// Gets the tenant that staged the message, or <see langword="null"/> when the envelope has no
    /// tenant. Durable stores persist this and restore it around dispatch.
    /// </summary>
    string? TenantId { get; }

    /// <summary>
    /// Returns a copy of this envelope stamped with <paramref name="tenantId"/>. Envelopes that
    /// cannot be copied keep their current value.
    /// </summary>
    /// <param name="tenantId">The tenant to stamp.</param>
    /// <returns>A tenant-stamped envelope.</returns>
    IDeferredEnvelope WithTenant(string? tenantId);

    /// <summary>
    /// Dispatches the command through <paramref name="services"/>.
    /// </summary>
    /// <param name="services">The scoped provider to resolve the broker from.</param>
    /// <param name="cancellationToken">Cancels the dispatch.</param>
    Task DispatchAsync(IServiceProvider services, CancellationToken cancellationToken);
}