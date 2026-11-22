//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.DeferredDelivery;

/// <summary>
/// Carries a durable row whose message version the reader cannot understand. Dispatch throws the
/// captured <see cref="Versioning.UnknownMessageVersionException"/>; the pump's normal failure path
/// moves it to the dead-letter store instead of dropping it.
/// </summary>
/// <param name="inner">The best-effort envelope of the stored command. Never <see langword="null"/>.</param>
/// <param name="error">The version failure to throw on dispatch. Never <see langword="null"/>.</param>
public sealed class UnknownVersionEnvelope(IDeferredEnvelope inner, Versioning.UnknownMessageVersionException error) : IDeferredEnvelope {
    private readonly IDeferredEnvelope _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <summary>Gets the captured version failure.</summary>
    public Versioning.UnknownMessageVersionException Error { get; } = error ?? throw new ArgumentNullException(nameof(error));

    /// <inheritdoc />
    public object Command => _inner.Command;

    /// <inheritdoc />
    public DateTimeOffset DueUtc => _inner.DueUtc;

    /// <inheritdoc />
    public DateTimeOffset? ExpiresAtUtc => _inner.ExpiresAtUtc;

    /// <inheritdoc />
    public int Version => _inner.Version;

    /// <inheritdoc />
    public string? TenantId => _inner.TenantId;

    /// <inheritdoc />
    public IDeferredEnvelope WithTenant(string? tenantId) =>
        new UnknownVersionEnvelope(_inner.WithTenant(tenantId), Error);

    /// <inheritdoc />
    public Task DispatchAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        throw Error;
}