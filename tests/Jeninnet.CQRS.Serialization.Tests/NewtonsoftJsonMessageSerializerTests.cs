//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Serialization.Tests;

/// <summary>Newtonsoft.Json uses the same camelCase web contract as the default JSON format.</summary>
[TestClass]
public sealed class NewtonsoftJsonMessageSerializerTests : SerializerContractTests {
    /// <inheritdoc />
    protected override IMessageSerializer CreateSerializer() =>
        new NewtonsoftJsonMessageSerializer(Options.Create(new NewtonsoftJsonMessageSerializerOptions()));

    /// <inheritdoc />
    protected override string ExpectedContentType => NewtonsoftJsonMessageSerializer.ContentTypeName;
}
