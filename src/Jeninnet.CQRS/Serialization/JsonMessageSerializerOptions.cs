//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Serialization;

/// <summary>
/// Options for <see cref="JsonMessageSerializer"/>.
/// </summary>
public sealed class JsonMessageSerializerOptions {
    /// <summary>
    /// Gets or sets the JSON options. Defaults to <see cref="JsonSerializerDefaults.Web"/>,
    /// which preserves the historical store behavior.
    /// </summary>
    public JsonSerializerOptions Serializer { get; set; } = new(JsonSerializerDefaults.Web);
}