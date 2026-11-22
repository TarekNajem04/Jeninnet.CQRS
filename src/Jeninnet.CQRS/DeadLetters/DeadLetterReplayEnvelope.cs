//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.DeadLetters;

/// <summary>
/// Wraps a dead-lettered envelope for replay and carries the dead-letter key through the pump,
/// so a successful execution removes the entry and a failure advances its attempt count.
/// </summary>
/// <remarks>
/// The original expiry is deliberately ignored: replay is an explicit operator action and must
/// not be silently dropped by the envelope's old time-to-live. Retention of the dead letter
/// itself is governed by <see cref="DeadLetterOptions.Retention"/>.
/// </remarks>
internal sealed class DeadLetterReplayEnvelope(IDeferredEnvelope inner, string deadLetterKey) : IDeferredEnvelope {
    internal string DeadLetterKey { get; } = deadLetterKey;

    public object Command => inner.Command;

    public DateTimeOffset DueUtc => inner.DueUtc;

    public DateTimeOffset? ExpiresAtUtc => null;

    public int Version => inner.Version;

    public string? TenantId => inner.TenantId;

    public IDeferredEnvelope WithTenant(string? tenantId) =>
        new DeadLetterReplayEnvelope(inner.WithTenant(tenantId), DeadLetterKey);

    public Task DispatchAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        inner.DispatchAsync(services, cancellationToken);
}