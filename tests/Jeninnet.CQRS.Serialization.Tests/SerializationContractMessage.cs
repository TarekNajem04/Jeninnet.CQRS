//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Serialization.Tests;

/// <summary>
/// The message every serializer contract test round-trips. MessagePack attributes make the
/// default standard resolver use a source-generated formatter, which is the AOT-safe path.
/// </summary>
[MessagePackObject]
public sealed record SerializationContractMessage(
    [property: Key(0)] string Name,
    [property: Key(1)] int Value,
    [property: Key(2)] string? Note);
