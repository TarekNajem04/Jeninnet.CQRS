//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.DeferredDelivery;

/// <summary>
/// Rehydrates durable message payloads into closed deferred envelopes without reflection.
/// The CQRS source generator emits <c>DeferredEnvelopeRegistry</c>, which implements this
/// interface for every command declared in the compiling assembly. Stores try the factory
/// first and fall back to annotated reflection for command types the registry does not know.
/// </summary>
/// <remarks>
/// A generated registry covers commands declared in the same compilation as <c>AddCqrs</c>.
/// Commands declared in a referenced contracts assembly are not covered; declare them in the
/// application assembly (or implement this interface there) for a fully trimmed deployment.
/// </remarks>
public interface IDeferredEnvelopeFactory {
    /// <summary>
    /// Tries to deserialize <paramref name="payload"/> as <paramref name="commandTypeName"/>
    /// and wrap it in the matching deferred envelope.
    /// </summary>
    /// <param name="commandTypeName">The stored command type name (assembly-qualified or full name).</param>
    /// <param name="payload">The serialized command payload.</param>
    /// <param name="dueUtc">When the command becomes due.</param>
    /// <param name="expiresAtUtc">When the entry expires, if ever.</param>
    /// <param name="version">The stored message schema version.</param>
    /// <param name="recurring">
    /// Persisted recurrence settings; <see langword="null"/> for a one-shot deferral. Recurrence
    /// is only supported for <see cref="ICommand"/> commands (not result commands).
    /// </param>
    /// <param name="envelope">The rebuilt envelope when the method returns <see langword="true"/>.</param>
    /// <returns>
    /// <see langword="true"/> when the command type is known and the payload produced an
    /// envelope; <see langword="false"/> when the caller should use its reflection fallback.
    /// </returns>
    bool TryCreate(
        string commandTypeName,
        ReadOnlySpan<byte> payload,
        DateTimeOffset dueUtc,
        DateTimeOffset? expiresAtUtc,
        int version,
        RecurringEnvelopeMetadata? recurring,
        [NotNullWhen(true)] out IDeferredEnvelope? envelope);

    /// <summary>
    /// Wraps an already-deserialized command, for example after a message upgrade replaced the
    /// stored instance. The command's exact runtime type must be known to the factory.
    /// </summary>
    /// <param name="command">The deserialized (and possibly upgraded) command.</param>
    /// <param name="dueUtc">When the command becomes due.</param>
    /// <param name="expiresAtUtc">When the entry expires, if ever.</param>
    /// <param name="version">The stored message schema version.</param>
    /// <param name="recurring">Persisted recurrence settings; <see langword="null"/> for a one-shot deferral.</param>
    /// <param name="envelope">The rebuilt envelope when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the command's runtime type is known.</returns>
    bool TryWrap(
        object command,
        DateTimeOffset dueUtc,
        DateTimeOffset? expiresAtUtc,
        int version,
        RecurringEnvelopeMetadata? recurring,
        [NotNullWhen(true)] out IDeferredEnvelope? envelope);
}
