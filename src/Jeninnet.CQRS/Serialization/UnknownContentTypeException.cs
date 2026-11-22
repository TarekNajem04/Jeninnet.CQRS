//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Serialization;

/// <summary>
/// Thrown when a payload declares a content type no registered serializer understands. The
/// message names both the offending content type and the registered ones so the consumer can
/// dead-letter the message with an actionable error instead of misparsing it.
/// </summary>
public sealed class UnknownContentTypeException : InvalidOperationException {
    /// <summary>Initializes the exception for an unknown wire content type.</summary>
    /// <param name="contentType">The normalized content type that was rejected.</param>
    /// <param name="supportedContentTypes">The content types the registry understands.</param>
    public UnknownContentTypeException(string contentType, IReadOnlyList<string> supportedContentTypes)
        : base($"No message serializer is registered for content type '{contentType}'. Registered content types: {string.Join(", ", supportedContentTypes)}. Register the matching format package or dead-letter the message.") {
        ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
        SupportedContentTypes = supportedContentTypes ?? throw new ArgumentNullException(nameof(supportedContentTypes));
    }

    /// <summary>Gets the normalized content type that was rejected.</summary>
    public string ContentType { get; }

    /// <summary>Gets the content types the registry understands.</summary>
    public IReadOnlyList<string> SupportedContentTypes { get; }
}
