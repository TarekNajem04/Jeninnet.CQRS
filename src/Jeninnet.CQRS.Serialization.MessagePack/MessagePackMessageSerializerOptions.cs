//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Serialization.MessagePack;

/// <summary>
/// Options for <see cref="MessagePackMessageSerializer"/>.
/// </summary>
public sealed class MessagePackMessageSerializerOptions {
    /// <summary>
    /// Gets or sets the MessagePack serializer options. Defaults to
    /// <see cref="MessagePackSerializerOptions.Standard"/>, which includes source-generated
    /// formatters for <c>[MessagePackObject]</c> types and is safe for native AOT.
    /// </summary>
    /// <remarks>
    /// To serialize unannotated contracts, opt into
    /// <c>MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance)</c>
    /// and supply a source-generated resolver before publishing with trimming or AOT.
    /// </remarks>
    public MessagePackSerializerOptions Serializer { get; set; } = MessagePackSerializerOptions.Standard;
}
