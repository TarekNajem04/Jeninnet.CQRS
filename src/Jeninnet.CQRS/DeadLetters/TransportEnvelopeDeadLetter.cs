//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.DeadLetters;

/// <summary>
/// A dead-letter view of a transport envelope that could not be read (for example an unknown
/// content type). The original metadata and payload stay inspectable, and replay fails with a
/// clear message because only the broker or the original command can be replayed.
/// </summary>
/// <param name="envelope">The unreadable transport envelope. Never <see langword="null"/>.</param>
public sealed class TransportEnvelopeDeadLetter(TransportEnvelope envelope) : IDeferredEnvelope {
    /// <summary>Gets the unreadable transport envelope.</summary>
    public TransportEnvelope Envelope { get; } = envelope ?? throw new ArgumentNullException(nameof(envelope));

    /// <summary>Gets the original envelope as the inspectable command payload.</summary>
    public object Command => Envelope;

    /// <summary>Gets when the transport accepted the message.</summary>
    public DateTimeOffset DueUtc => Envelope.Timestamp;

    /// <summary>Gets that a raw transport envelope never expires on its own.</summary>
    public DateTimeOffset? ExpiresAtUtc => null;

    /// <summary>Gets the message schema version carried by the original envelope.</summary>
    public int Version => Envelope.MessageVersion;

    /// <summary>Gets the tenant carried by the original transport envelope.</summary>
    public string? TenantId => Envelope.TenantId;

    /// <summary>Returns this wrapper; the tenant comes from the immutable transport envelope.</summary>
    /// <param name="tenantId">The tenant to stamp (ignored).</param>
    /// <returns>This envelope.</returns>
    public IDeferredEnvelope WithTenant(string? tenantId) => this;

    /// <summary>
    /// Always fails: a raw transport envelope has no command to dispatch. Replay the broker
    /// message after fixing the content type, or replay the original command.
    /// </summary>
    /// <param name="services">The scoped provider (unused).</param>
    /// <param name="cancellationToken">Cancels the replay.</param>
    public Task DispatchAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            $"A raw transport envelope for '{Envelope.MessageType}' cannot be replayed directly. Replay the broker message or the original command.");
}