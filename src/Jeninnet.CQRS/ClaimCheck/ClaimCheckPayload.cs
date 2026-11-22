//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.ClaimCheck;

/// <summary>
/// A claimed payload plus the content type it was written with.
/// </summary>
/// <param name="Payload">The serialized bytes.</param>
/// <param name="ContentType">The serializer content type that produced the bytes.</param>
public sealed record ClaimCheckPayload(byte[] Payload, string ContentType);
