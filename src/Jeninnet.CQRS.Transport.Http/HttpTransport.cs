//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Http;

/// <summary>
/// Outbound <see cref="IMessageTransport"/> that POSTs the canonical envelope to a remote HTTP
/// endpoint. Authentication is configuration (API key and/or HMAC-SHA256); retries intentionally
/// stay with the outbox relay instead of being duplicated inside the adapter.
/// </summary>
/// <remarks>
/// The remote endpoint receives the serialized payload as the request body and the envelope
/// metadata as headers described by <see cref="HttpTransportHeaders"/> and
/// <see cref="TransportHeaders"/>. A non-success response throws
/// <see cref="HttpTransportException"/>; 401 and 403 add an explicit authentication message so
/// relay failures are diagnosable.
/// </remarks>
public sealed class HttpTransport : IMessageTransport {
    private readonly HttpTransportOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TransportNamingConvention _namingConvention;
    private readonly TimeProvider _clock;
    private readonly TenantAccessor? _tenant;
    private readonly MessageObserverInvoker? _observers;
    private readonly TransportEnvelopeBuilder _envelopes;
    private readonly object _sync = new(); // NOSONAR: System.Threading.Lock is unavailable on netstandard2.0/net462; the object lock is the compatible shim.
    private bool _started;

    /// <summary>Initializes the transport.</summary>
    /// <param name="options">Endpoint and authentication options.</param>
    /// <param name="httpClientFactory">Creates the named client; the transport never disposes it.</param>
    /// <param name="serializer">Serializes payloads and supplies the content type.</param>
    /// <param name="namingConvention">Resolves exchange/queue/topic names carried in headers.</param>
    /// <param name="clock">Stamps envelopes; defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="tenant">The ambient tenant adapter; <see langword="null"/> without tenancy.</param>
    /// <param name="observers">Send and publish observers; <see langword="null"/> when not configured.</param>
    public HttpTransport(
        IOptions<HttpTransportOptions> options,
        IHttpClientFactory httpClientFactory,
        IMessageSerializer serializer,
        TransportNamingConvention namingConvention,
        TimeProvider? clock = null,
        TenantAccessor? tenant = null,
        MessageObserverInvoker? observers = null) {
        _options = Validate((options ?? throw new ArgumentNullException(nameof(options))).Value);
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        var payloadSerializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _namingConvention = namingConvention ?? throw new ArgumentNullException(nameof(namingConvention));
        _clock = clock ?? TimeProvider.System;
        _tenant = tenant;
        _observers = observers;
        _envelopes = TransportEnvelopeBuilders.ForTenantAware(
            payloadSerializer,
            (messageType, _) => _namingConvention.Describe(messageType),
            () => _tenant?.Current,
            () => _clock.GetUtcNow());
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default) {
        lock(_sync) {
            _started = true;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default) {
        lock(_sync) {
            _started = false;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task SendAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) {
        Guard.NotNull(message);
        Guard.NotNull(messageType);

        var envelope = CreateEnvelope(message, messageType, options, TransportDeliveryMode.Send);

        await TransportOperation.SendAsync(
            _observers,
            message,
            messageType,
            envelope,
            ct => PostAsync(envelope, ct),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PublishAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) {
        Guard.NotNull(message);
        Guard.NotNull(messageType);

        var envelope = CreateEnvelope(message, messageType, options, TransportDeliveryMode.Publish);

        await TransportOperation.PublishAsync(
            _observers,
            message,
            messageType,
            envelope,
            ct => PostAsync(envelope, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task PostAsync(TransportEnvelope envelope, CancellationToken cancellationToken) {
        EnsureStarted();

        var payload = envelope.Payload.ToArray();
        using var client = _httpClientFactory.CreateClient(_options.HttpClientName);
        client.Timeout = _options.Timeout;
        using var request = CreateRequest(envelope, payload);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if(response.IsSuccessStatusCode) {
            return;
        }

        var body = await ReadBodyAsync(response).ConfigureAwait(false);
        var status = (int)response.StatusCode;

        if(response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden) {
            throw new HttpTransportException(
                response.StatusCode,
                body,
                $"HTTP transport authentication failed with status {status} ({response.StatusCode}). {FormatBody(body)}".Trim());
        }

        throw new HttpTransportException(
            response.StatusCode,
            body,
            $"The HTTP transport endpoint rejected the message with status {status} ({response.StatusCode}). {FormatBody(body)}".Trim());
    }

    private HttpRequestMessage CreateRequest(TransportEnvelope envelope, byte[] payload) {
        var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint) {
            Content = new ByteArrayContent(payload),
        };

        request.Content.Headers.TryAddWithoutValidation("Content-Type", envelope.ContentType);

        foreach(var pair in HttpTransportWire.CreateHeaders(envelope)) {
            if(string.Equals(pair.Key, "content-type", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            request.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
        }

        if(!StringCompat.IsNullOrWhiteSpace(_options.ApiKey)) {
            request.Headers.TryAddWithoutValidation(_options.ApiKeyHeader, _options.ApiKey);
        }

        if(!StringCompat.IsNullOrWhiteSpace(_options.HmacSecret)) {
            request.Headers.TryAddWithoutValidation(_options.HmacHeader, HttpTransportWire.ComputeSignature(payload, _options.HmacSecret));
        }

        return request;
    }

    private TransportEnvelope CreateEnvelope(object message, Type messageType, TransportMessageOptions? options, TransportDeliveryMode deliveryMode) =>
        _envelopes.Create(message, messageType, options, deliveryMode);

    private void EnsureStarted() {
        lock(_sync) {
            if(!_started) {
                throw new InvalidOperationException("The HTTP transport is not started. Call StartAsync before sending or publishing.");
            }
        }
    }

    private static async Task<string?> ReadBodyAsync(HttpResponseMessage response) {
        if(response.Content is null) {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        return body.Length <= 2048 ? body : body.Substring(0, 2048);
    }

    private static string FormatBody(string? body) =>
        StringCompat.IsNullOrWhiteSpace(body) ? string.Empty : $"Response: {body}";

    private static HttpTransportOptions Validate(HttpTransportOptions options) {
        Guard.NotNull(options);

        if(StringCompat.IsNullOrWhiteSpace(options.Endpoint)) {
            throw new ArgumentException("Endpoint must not be empty.", nameof(options));
        }

        if(StringCompat.IsNullOrWhiteSpace(options.HttpClientName)) {
            throw new ArgumentException("HttpClientName must not be empty.", nameof(options));
        }

        if(StringCompat.IsNullOrWhiteSpace(options.ApiKeyHeader)) {
            throw new ArgumentException("ApiKeyHeader must not be empty.", nameof(options));
        }

        if(StringCompat.IsNullOrWhiteSpace(options.HmacHeader)) {
            throw new ArgumentException("HmacHeader must not be empty.", nameof(options));
        }

        if(options.Timeout <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options), options.Timeout, "Timeout must be greater than zero.");
        }

        return options;
    }
}