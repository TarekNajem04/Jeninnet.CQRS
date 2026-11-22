//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas;

/// <summary>
/// A persisted timeout payload. The deferred scheduler stores this command; its handler rebuilds
/// the original event and feeds it back into <see cref="SagaDispatcher"/>. The event type is kept
/// as a string so the command is serializable by any configured message serializer.
/// </summary>
/// <param name="EventTypeName">The assembly-qualified timeout event type name.</param>
/// <param name="ContentType">The payload media type.</param>
/// <param name="Payload">The serialized timeout event.</param>
public sealed record SagaTimeoutCommand(
    string EventTypeName,
    string ContentType,
    byte[] Payload) : ICommand;

/// <summary>
/// Rebuilds a scheduled saga timeout event and dispatches it through the saga machine. Registered
/// by <c>AddCqrsSagas&lt;TSaga&gt;()</c>; it requires a deferred dispatcher (for example
/// <c>AddCqrsDeferredDelivery()</c>) to execute the persisted command.
/// </summary>
/// <param name="dispatcher">The saga dispatcher. Never <see langword="null"/>.</param>
/// <param name="serializers">The content-type registry. Never <see langword="null"/>.</param>
public sealed class SagaTimeoutCommandHandler(SagaDispatcher dispatcher, IMessageSerializerRegistry serializers) : ICommandHandler<SagaTimeoutCommand> {
    private readonly SagaDispatcher _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    private readonly IMessageSerializerRegistry _serializers = serializers ?? throw new ArgumentNullException(nameof(serializers));

    /// <inheritdoc />
    public async Task<Unit> HandleAsync(SagaTimeoutCommand command, CancellationToken cancellationToken = default) {
        Guard.NotNull(command);

        var eventType = _dispatcher.ResolveEventType(command.EventTypeName)
            ?? throw new InvalidOperationException(
                $"The scheduled saga timeout event '{command.EventTypeName}' is not handled by any configured machine.");
        var message = _serializers.Deserialize(command.Payload, eventType, command.ContentType)
            ?? throw new InvalidOperationException(
                $"The scheduled saga timeout event '{command.EventTypeName}' could not be deserialized.");

        await _dispatcher.DispatchAsync(message, eventType, cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }
}
