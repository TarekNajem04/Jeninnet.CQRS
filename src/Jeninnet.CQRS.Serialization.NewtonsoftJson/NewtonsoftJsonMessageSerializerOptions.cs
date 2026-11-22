//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Serialization.NewtonsoftJson;

/// <summary>
/// Options for <see cref="NewtonsoftJsonMessageSerializer"/>. The defaults mirror the web JSON
/// contract used by <c>System.Text.Json</c>: camelCase property names and case-insensitive reads,
/// so both JSON formats are interchangeable on the wire.
/// </summary>
public sealed class NewtonsoftJsonMessageSerializerOptions {
    /// <summary>
    /// Gets or sets the Newtonsoft.Json settings. Mutate before the serializer is resolved;
    /// implementations are singletons and the settings instance is not cloned.
    /// </summary>
    public JsonSerializerSettings Serializer { get; set; } = new() {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        MissingMemberHandling = MissingMemberHandling.Ignore,
    };
}