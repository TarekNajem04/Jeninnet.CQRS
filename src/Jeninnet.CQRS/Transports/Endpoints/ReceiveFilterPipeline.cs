//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// The consume-side pipeline every transport adapter calls for an inbound envelope. It resolves the
/// endpoint policy, runs registered receive filters in order, then runs consumer middleware around
/// the handler inside the endpoint's retry and consumption-timeout policy.
/// </summary>
/// <remarks>
/// Filters run before the handler and outside the retry loop: a rejected delivery never reaches
/// the handler, and a filter that throws is treated as a handler failure. Payloads are decoded only
/// when a filter applies, so endpoints without filters keep their existing zero-deserialization
/// fast path. The pipeline is transport-neutral; the in-memory and broker adapters all call the
/// same method. A transport that fans one envelope out to several local handlers invokes the
/// pipeline once per handler, so a filter runs once per subscriber.
/// </remarks>
public sealed class ReceiveFilterPipeline {
    private readonly IServiceScopeFactory _scopes;
    private readonly EndpointConfigurationRegistry _endpoints;
    private readonly TransportMessageReader _reader;
    private readonly IReceiveMiddleware[] _middlewares;
    private readonly MessageObserverInvoker? _observers;

    /// <summary>Initializes the pipeline.</summary>
    /// <param name="scopes">Creates a scope per filtered delivery so scoped filters dispose correctly.</param>
    /// <param name="endpoints">The immutable endpoint/filter snapshot.</param>
    /// <param name="reader">Decodes the payload with the envelope's negotiated content type.</param>
    /// <param name="middlewares">Consumer middleware in registration order; empty when none are registered.</param>
    /// <param name="observers">Consume observers; <see langword="null"/> when observation is not configured.</param>
    public ReceiveFilterPipeline(
        IServiceScopeFactory scopes,
        EndpointConfigurationRegistry endpoints,
        TransportMessageReader reader,
        IEnumerable<IReceiveMiddleware>? middlewares = null,
        MessageObserverInvoker? observers = null) {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _middlewares = middlewares?.ToArray() ?? [];
        _observers = observers;
    }

    /// <summary>
    /// Applies filters and invokes <paramref name="handler"/> for one delivery.
    /// </summary>
    /// <param name="handler">The consumer handler owned by the transport.</param>
    /// <param name="envelope">The delivered envelope.</param>
    /// <param name="cancellationToken">Cancels filtering, retries, and the handler.</param>
    /// <returns>
    /// <see langword="true"/> when the handler ran; <see langword="false"/> when a filter dropped
    /// the delivery. A transport should acknowledge a dropped delivery rather than retrying it.
    /// </returns>
    public async Task<bool> HandleAsync(
        IMessageTransportHandler handler,
        TransportEnvelope envelope,
        CancellationToken cancellationToken = default) {
        Guard.NotNull(handler);
        Guard.NotNull(envelope);

        var messageType = ResolveMessageType(envelope.MessageType);
        var endpoint = messageType is null ? null : _endpoints.Find(messageType);
        var observation = _observers is { HasConsumeObservers: true }
            ? new ConsumeObservationContext(envelope, endpoint)
            : null;

        if(observation is not null) {
            await _observers!.OnConsumingAsync(observation, cancellationToken).ConfigureAwait(false);
        }

        try {
            var handled = await ProcessCoreAsync(handler, envelope, messageType, endpoint, cancellationToken).ConfigureAwait(false);

            if(handled && observation is not null) {
                await _observers!.OnConsumedAsync(observation, cancellationToken).ConfigureAwait(false);
            }

            return handled;
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch(Exception error) {
            if(observation is not null) {
                await _observers!.OnConsumeFaultAsync(observation, error, CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
    }

    private async Task<bool> ProcessCoreAsync(
        IMessageTransportHandler handler,
        TransportEnvelope envelope,
        Type? messageType,
        EndpointConfiguration? endpoint,
        CancellationToken cancellationToken) {
        if(messageType is not null) {
            var filters = _endpoints.GetFilters(messageType, endpoint?.Name);

            if(filters.Count > 0) {
                var message = _reader.Deserialize(envelope, messageType);

                using var scope = _scopes.CreateScope();
                var context = new ReceiveContext(message, envelope, endpoint ?? EndpointConfiguration.DefaultFor(messageType));

                foreach(var filterType in filters) {
                    var filter = (IReceiveFilter)scope.ServiceProvider.GetRequiredService(filterType);

                    if(!await filter.FilterAsync(context, cancellationToken).ConfigureAwait(false)) {
                        return false;
                    }
                }
            }
        }

        if(_middlewares.Length is 0) {
            await InvokeWithPolicyAsync(handler, envelope, endpoint, cancellationToken).ConfigureAwait(false);

            return true;
        }

        var middlewareContext = new ReceiveMiddlewareContext(envelope, endpoint);
        Func<CancellationToken, Task<bool>> chain = async token => {
            await InvokeWithPolicyAsync(handler, envelope, endpoint, token).ConfigureAwait(false);

            return true;
        };

        for(var index = _middlewares.Length - 1; index >= 0; index--) {
            var middleware = _middlewares[index];
            var next = chain;
            chain = token => middleware.InvokeAsync(middlewareContext, next, token);
        }

        return await chain(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InvokeWithPolicyAsync(
        IMessageTransportHandler handler,
        TransportEnvelope envelope,
        EndpointConfiguration? endpoint,
        CancellationToken cancellationToken) {
        if(endpoint is null) {
            await handler.HandleAsync(envelope, cancellationToken).ConfigureAwait(false);

            return;
        }

        var attempts = Math.Max(1, endpoint.Retry.MaxAttempts);

        for(var attempt = 1; ; attempt++) { // NOSONAR: The loop exits via return/throw; the attempt counter drives backoff, not the stop condition.
            try {
                await InvokeOnceAsync(handler, envelope, endpoint, cancellationToken).ConfigureAwait(false);

                return;
            }
            catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch when(attempt < attempts) {
                await Task.Delay(endpoint.Retry.GetDelay(attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task InvokeOnceAsync(
        IMessageTransportHandler handler,
        TransportEnvelope envelope,
        EndpointConfiguration endpoint,
        CancellationToken cancellationToken) {
        if(endpoint.ConsumptionTimeout == Timeout.InfiniteTimeSpan) {
            await handler.HandleAsync(envelope, cancellationToken).ConfigureAwait(false);

            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(endpoint.ConsumptionTimeout);

        try {
            await handler.HandleAsync(envelope, timeout.Token).ConfigureAwait(false);
        }
        catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested) {
            throw new TimeoutException(
                $"Endpoint '{endpoint.Name}' consumption timed out after {endpoint.ConsumptionTimeout}.");
        }
    }

    private static Type? ResolveMessageType(string messageType) {
        if(StringCompat.IsNullOrWhiteSpace(messageType)) {
            return null;
        }

        var resolved = Type.GetType(messageType, throwOnError: false);

        if(resolved is not null) {
            return resolved;
        }

        foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            resolved = assembly.GetType(messageType, throwOnError: false);

            if(resolved is not null) {
                return resolved;
            }
        }

        return null;
    }
}