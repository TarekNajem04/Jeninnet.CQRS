//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>
/// Rebuilds notification envelopes for the EF Core outbox. Runtime type resolution lives in
/// this package (outside the NativeAOT story); rows whose notification type no longer resolves
/// are dropped because they can never be delivered.
/// </summary>
internal static class OutboxEnvelopeSerializer {
    /// <summary>Rebuilds a closed notification envelope, or returns <see langword="null"/> for ghosts.</summary>
    internal static IDeferredEnvelope? Rebuild(string notificationTypeName, ReadOnlySpan<byte> payload, DateTimeOffset stagedAtUtc, IMessageSerializer serializer, string? tenantId = null) {
        var notificationType = Type.GetType(notificationTypeName, throwOnError: false);

        if(notificationType is null || !typeof(INotification).IsAssignableFrom(notificationType)) {
            return null;
        }

        object? notification;
        try {
            notification = serializer.Deserialize(payload, notificationType);
        }
        catch(JsonException) {
            return null;
        }

        if(notification is null) {
            return null;
        }

        var envelopeType = typeof(OutboxNotification<>).MakeGenericType(notificationType);

        return (IDeferredEnvelope)Activator.CreateInstance(envelopeType, notification, stagedAtUtc, tenantId)!;
    }
}