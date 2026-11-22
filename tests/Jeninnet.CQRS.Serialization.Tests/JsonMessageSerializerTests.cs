//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Serialization.Tests;

/// <summary>The default System.Text.Json format keeps its historical web-defaults contract.</summary>
[TestClass]
public sealed class JsonMessageSerializerTests : SerializerContractTests {
    /// <inheritdoc />
    protected override IMessageSerializer CreateSerializer() =>
        new JsonMessageSerializer(Options.Create(new JsonMessageSerializerOptions()));

    /// <inheritdoc />
    protected override string ExpectedContentType => JsonMessageSerializer.ContentTypeName;
}