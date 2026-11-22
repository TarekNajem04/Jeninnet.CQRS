//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Http;

/// <summary>
/// Configuration for the outbound HTTP transport. The endpoint receives the serialized payload in
/// the request body and all envelope metadata in headers; the transport never retries, so the
/// outbox relay remains the single owner of retry policy.
/// </summary>
public sealed class HttpTransportOptions {
    /// <summary>The named <see cref="IHttpClientFactory"/> client used by default.</summary>
    public const string DefaultHttpClientName = "Jeninnet.CQRS.Transport.Http";

    /// <summary>
    /// Gets or sets the absolute endpoint, or a relative path resolved against the registered
    /// HTTP client's base address.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Gets or sets the named HTTP client; defaults to <see cref="DefaultHttpClientName"/>.</summary>
    public string HttpClientName { get; set; } = DefaultHttpClientName;

    /// <summary>Gets or sets an optional API key sent on every request.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Gets or sets the API-key header name. Defaults to <c>x-api-key</c>.</summary>
    public string ApiKeyHeader { get; set; } = HttpTransportHeaders.ApiKey;

    /// <summary>
    /// Gets or sets an optional HMAC-SHA256 secret. When set, every request carries a
    /// <c>sha256=&lt;hex&gt;</c> signature header computed over the raw body.
    /// </summary>
    public string? HmacSecret { get; set; }

    /// <summary>Gets or sets the signature header name. Defaults to <c>x-cqrs-signature</c>.</summary>
    public string HmacHeader { get; set; } = HttpTransportHeaders.Signature;

    /// <summary>Gets or sets the request timeout. Defaults to 30 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
