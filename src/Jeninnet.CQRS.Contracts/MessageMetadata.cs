//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS;

/// <summary>
/// Declares the schema version of a message contract. The version is metadata for producers,
/// consumers, and versioning guides; it does not change serialization by itself.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class MessageVersionAttribute : Attribute {
    /// <summary>Initializes the attribute with a schema version.</summary>
    /// <param name="version">The schema version; must be at least 1.</param>
    public MessageVersionAttribute(int version) {
        if(version < 1) {
            throw new ArgumentOutOfRangeException(nameof(version), version, "Message version must be at least 1.");
        }

        Version = version;
    }

    /// <summary>Gets the schema version.</summary>
    public int Version { get; }
}

/// <summary>
/// Transport-neutral header names shared by producers and consumers. The core transport package
/// keeps its historical <c>TransportHeaders</c> type as an alias of these values.
/// </summary>
public static class MessageHeaders {
    /// <summary>The assembly-qualified or full message type name.</summary>
    public const string MessageType = "message-type";

    /// <summary>The integer message schema version; defaults to 1 when absent.</summary>
    public const string MessageVersion = "message-version";

    /// <summary>The payload media type negotiated by the consuming serializer.</summary>
    public const string ContentType = "content-type";

    /// <summary>The tenant partition key carried across the wire.</summary>
    public const string Tenant = "x-tenant-id";

    /// <summary>The historical message type header emitted before the metadata was standardized.</summary>
    public const string LegacyMessageType = "x-message-type";
}