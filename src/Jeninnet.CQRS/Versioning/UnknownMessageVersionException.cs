//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Versioning;

/// <summary>
/// A durable row was written at a message version the reader cannot understand: newer than every
/// registered upgrader, or a gap in the upgrade chain. Deferred delivery turns this into a
/// dead-letter entry instead of dropping the row.
/// </summary>
public sealed class UnknownMessageVersionException : InvalidOperationException {
    /// <summary>
    /// Initializes the exception.
    /// </summary>
    /// <param name="messageType">The stored message type.</param>
    /// <param name="storedVersion">The version found on the row.</param>
    /// <param name="currentVersion">The highest version the reader supports.</param>
    public UnknownMessageVersionException(Type messageType, int storedVersion, int currentVersion)
        : base(BuildMessage(messageType, storedVersion, currentVersion)) {
        MessageType = messageType;
        StoredVersion = storedVersion;
        CurrentVersion = currentVersion;
    }

    private static string BuildMessage(Type messageType, int storedVersion, int currentVersion) {
        Guard.NotNull(messageType);

        return $"Message version {storedVersion} of '{messageType.Name}' cannot be read; the current reader supports up to version {currentVersion}.";
    }

    /// <summary>Gets the stored message type.</summary>
    public Type MessageType { get; }

    /// <summary>Gets the version found on the row.</summary>
    public int StoredVersion { get; }

    /// <summary>Gets the highest version the reader supports.</summary>
    public int CurrentVersion { get; }
}