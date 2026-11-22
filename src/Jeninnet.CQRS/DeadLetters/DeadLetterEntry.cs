//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.DeadLetters;

/// <summary>
/// One dead-lettered message: the original envelope, how many attempts failed, and the last
/// error observed. Keys are assigned by the store and are used by
/// <see cref="IDeferredDispatcher.ReplayAsync"/>.
/// </summary>
public sealed class DeadLetterEntry(
    string key,
    IDeferredEnvelope envelope,
    int attempts,
    string lastErrorType,
    string lastErrorMessage,
    DateTimeOffset firstFailedAtUtc,
    DateTimeOffset lastFailedAtUtc) {
    /// <summary>Gets the store-assigned key.</summary>
    public string Key { get; } = key ?? throw new ArgumentNullException(nameof(key));

    /// <summary>Gets the original envelope, used to re-stage the command on replay.</summary>
    public IDeferredEnvelope Envelope { get; } = envelope ?? throw new ArgumentNullException(nameof(envelope));

    /// <summary>Gets how many executions have failed for this entry.</summary>
    public int Attempts { get; } = attempts;

    /// <summary>Gets the full name of the last exception type.</summary>
    public string LastErrorType { get; } = lastErrorType ?? throw new ArgumentNullException(nameof(lastErrorType));

    /// <summary>Gets the message of the last exception.</summary>
    public string LastErrorMessage { get; } = lastErrorMessage ?? throw new ArgumentNullException(nameof(lastErrorMessage));

    /// <summary>Gets when the first failure was observed, in UTC.</summary>
    public DateTimeOffset FirstFailedAtUtc { get; } = firstFailedAtUtc;

    /// <summary>Gets when the most recent failure was observed, in UTC.</summary>
    public DateTimeOffset LastFailedAtUtc { get; } = lastFailedAtUtc;

    /// <summary>Gets the full name of the message type, or the stored type name when the command could not be resolved.</summary>
    public string CommandType => Envelope switch {
        TransportEnvelopeDeadLetter transport => transport.Envelope.MessageType,
        UnresolvedCommandEnvelope unresolved => unresolved.CommandTypeName,
        _ => Envelope.Command.GetType().FullName ?? Envelope.Command.GetType().Name,
    };

    /// <summary>Returns a copy with the failure information advanced.</summary>
    /// <param name="error">The new failure.</param>
    /// <param name="failedAtUtc">When the failure was observed.</param>
    internal DeadLetterEntry FailedAgain(Exception error, DateTimeOffset failedAtUtc) =>
        new(Key, Envelope, Attempts + 1, ErrorTypeOf(error), error.Message, FirstFailedAtUtc, failedAtUtc);

    /// <summary>Returns the full type name used to persist an error type.</summary>
    /// <param name="error">The exception to describe.</param>
    public static string ErrorTypeOf(Exception error) => error.GetType().FullName ?? error.GetType().Name;
}