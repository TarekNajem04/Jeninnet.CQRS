//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Serialization.Tests;

/// <summary>MessagePack round-trips through the AOT-safe standard resolver.</summary>
[TestClass]
public sealed class MessagePackMessageSerializerTests : SerializerContractTests {
    /// <inheritdoc />
    protected override IMessageSerializer CreateSerializer() =>
        new MessagePackMessageSerializer(Options.Create(new MessagePackMessageSerializerOptions()));

    /// <inheritdoc />
    protected override string ExpectedContentType => MessagePackMessageSerializer.ContentTypeName;
}
