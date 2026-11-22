//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.RequestResponse;

/// <summary>
/// Transport handler for request/response. Requests are published envelopes carrying the kind
/// header; every matching responder runs and publishes its response with the same correlation id.
/// Responses complete the local waiter (first or all) and are otherwise ignored.
/// </summary>
internal sealed class RequestResponseTransportHandler(
    IServiceProvider provider,
    IEnumerable<IRequestResponder> responders,
    RequestWaiterRegistry waiters,
    IRequestInbox inbox,
    IMessageSerializerRegistry serializers,
    TenantAccessor? tenant = null) : IMessageTransportHandler {
    private readonly IServiceProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    private readonly IRequestResponder[] _responders = responders?.ToArray() ?? [];
    private readonly RequestWaiterRegistry _waiters = waiters ?? throw new ArgumentNullException(nameof(waiters));
    private readonly IRequestInbox _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
    private readonly IMessageSerializerRegistry _serializers = serializers ?? throw new ArgumentNullException(nameof(serializers));
    private readonly TenantAccessor? _tenant = tenant;
    private IMessageTransport? _transport;

    private IMessageTransport Transport =>
        _transport ??= _provider.GetRequiredService<IMessageTransport>();

    /// <inheritdoc />
    public async Task HandleAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default) {
        Guard.NotNull(envelope);

        if(!envelope.Headers.TryGetValue(RequestResponseHeaders.Kind, out var kind)) {
            return;
        }

        using var tenantScope = _tenant?.EnterScope(envelope.TenantId);

        if(string.Equals(kind, RequestResponseHeaders.Request, StringComparison.Ordinal)) {
            await HandleRequestAsync(envelope, cancellationToken).ConfigureAwait(false);
        } else if(string.Equals(kind, RequestResponseHeaders.Response, StringComparison.Ordinal)) {
            await HandleResponseAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleRequestAsync(TransportEnvelope envelope, CancellationToken cancellationToken) {
        if(StringCompat.IsNullOrWhiteSpace(envelope.CorrelationId)) {
            return;
        }

        foreach(var responder in _responders) {
            if(!Matches(responder.RequestType, envelope.MessageType)) {
                continue;
            }

            var request = _serializers.Deserialize(envelope.Payload.Span, responder.RequestType, envelope.ContentType);

            if(request is null) {
                continue;
            }

            var response = await responder.RespondAsync(request, cancellationToken).ConfigureAwait(false);

            if(response is null) {
                continue;
            }

            var headers = new Dictionary<string, string>(StringComparer.Ordinal) {
                [RequestResponseHeaders.Kind] = RequestResponseHeaders.Response,
                [RequestResponseHeaders.RequestId] = envelope.CorrelationId,
            };

            await Transport.PublishAsync(
                response,
                response.GetType(),
                new TransportMessageOptions {
                    CorrelationId = envelope.CorrelationId,
                    TenantId = _tenant?.Resolve(envelope.TenantId),
                    Headers = headers,
                },
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleResponseAsync(TransportEnvelope envelope, CancellationToken cancellationToken) {
        if(StringCompat.IsNullOrWhiteSpace(envelope.CorrelationId) || !_waiters.TryGet(envelope.CorrelationId, out var waiter)) {
            return;
        }

        var response = _serializers.Deserialize(envelope.Payload.Span, waiter.ResponseType, envelope.ContentType);

        if(response is null) {
            return;
        }

        waiter.Complete(response);

        if(waiter.Mode is RequestResponseMode.First) {
            await _inbox.RemoveAsync(envelope.CorrelationId, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool Matches(Type requestType, string messageTypeName) =>
        string.Equals(requestType.AssemblyQualifiedName, messageTypeName, StringComparison.Ordinal) ||
        string.Equals(requestType.FullName, messageTypeName, StringComparison.Ordinal) ||
        string.Equals(requestType.Name, messageTypeName, StringComparison.Ordinal);
}
