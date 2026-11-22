//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore;

/// <summary>
/// Rebuilds deferred envelopes for the EF Core stores. Rehydration asks the generated
/// <see cref="IDeferredEnvelopeFactory"/> first; only command types the registry does not know
/// reach the reflection fallback. That fallback is annotated so a trimmed application sees the
/// real requirement instead of a runtime surprise.
/// </summary>
internal static class DeferredEnvelopeSerializer {
    /// <summary>
    /// Rebuilds a closed envelope at <paramref name="version"/>, applying the upgrade policy.
    /// Rows whose version is unknown come back as <see cref="UnknownVersionEnvelope"/> so the
    /// pump can dead-letter them; unresolved types return <see langword="null"/>.
    /// </summary>
    internal static IDeferredEnvelope? Rebuild( // NOSONAR: Public options/records intentionally group related settings; splitting would fragment the API.
        string commandTypeName,
        ReadOnlySpan<byte> payload,
        DateTimeOffset dueUtc,
        DateTimeOffset? expiresAtUtc,
        IMessageSerializer serializer,
        int version = 1,
        MessageUpgradePolicy? upgrades = null,
        RecurringEnvelopeMetadata? recurring = null,
        IDeferredEnvelopeFactory? factory = null,
        string? tenantId = null) {
        if(commandTypeName == typeof(TransportEnvelope).AssemblyQualifiedName || commandTypeName == typeof(TransportEnvelope).FullName) {
            return serializer.Deserialize(payload, typeof(TransportEnvelope)) is TransportEnvelope transportEnvelope
                ? new TransportEnvelopeDeadLetter(transportEnvelope)
                : null;
        }

        if(factory is not null) {
            try {
                if(factory.TryCreate(commandTypeName, payload, dueUtc, expiresAtUtc, version, recurring, out var generated)) {
                    return ApplyUpgrades(generated, upgrades, dueUtc, expiresAtUtc, version, recurring, factory).WithTenant(tenantId);
                }
            }
            catch(Exception exception) when(exception is JsonException or ArgumentException or InvalidTimeZoneException or TimeZoneNotFoundException or InvalidOperationException) {
                // The registry knew the type but the row cannot be rebuilt (corrupt payload or
                // invalid recurrence metadata). Treat it exactly like a corrupt reflected row.
                return null;
            }
        }

        return RebuildByReflection(commandTypeName, payload, dueUtc, expiresAtUtc, serializer, version, upgrades, recurring)?.WithTenant(tenantId);
    }

    private static IDeferredEnvelope ApplyUpgrades(
        IDeferredEnvelope envelope,
        MessageUpgradePolicy? upgrades,
        DateTimeOffset dueUtc,
        DateTimeOffset? expiresAtUtc,
        int version,
        RecurringEnvelopeMetadata? recurring,
        IDeferredEnvelopeFactory factory) {
        if(upgrades is null) {
            return envelope;
        }

        var command = envelope.Command;

        try {
            var upgraded = upgrades.Upgrade(command, command.GetType(), version);

            if(ReferenceEquals(upgraded, command)) {
                return envelope;
            }

            return factory.TryWrap(upgraded, dueUtc, expiresAtUtc, version, recurring, out var rewrapped)
                ? rewrapped
                : throw new InvalidOperationException(
                    $"The generated deferred-envelope registry does not cover the upgraded command type '{upgraded.GetType().FullName}'. An upgrader must preserve the message type.");
        }
        catch(UnknownMessageVersionException exception) {
            return new UnknownVersionEnvelope(envelope, exception);
        }
    }

    [RequiresUnreferencedCode("Falls back to Type.GetType for command types the generated deferred-envelope registry does not cover. Declare the command in the application assembly (with the CQRS source generator) for a fully trimmed deployment.")]
    [RequiresDynamicCode("Falls back to MakeGenericType and Activator.CreateInstance for envelope construction when no generated registry covers the command type.")]
    private static IDeferredEnvelope? RebuildByReflection( // NOSONAR: Public options/records intentionally group related settings; splitting would fragment the API.
        string commandTypeName,
        ReadOnlySpan<byte> payload,
        DateTimeOffset dueUtc,
        DateTimeOffset? expiresAtUtc,
        IMessageSerializer serializer,
        int version,
        MessageUpgradePolicy? upgrades,
        RecurringEnvelopeMetadata? recurring) {
        var commandType = Type.GetType(commandTypeName, throwOnError: false);

        if(commandType is null) {
            // Keep the accepted message inspectable: the pump dead-letters it instead of
            // dropping a row that may only be missing from a trimmed contract assembly.
            return new UnresolvedCommandEnvelope(commandTypeName, dueUtc, expiresAtUtc, version);
        }

        object? command;
        try {
            command = serializer.Deserialize(payload, commandType);
        }
        catch(JsonException) {
            return null;
        }

        if(command is null) {
            return null;
        }

        if(upgrades is null) {
            return CreateForVersion(command, commandType, dueUtc, expiresAtUtc, version, recurring);
        }

        try {
            var upgraded = upgrades.Upgrade(command, commandType, version);

            return CreateForVersion(upgraded, commandType, dueUtc, expiresAtUtc, version, recurring);
        }
        catch(UnknownMessageVersionException exception) {
            return new UnknownVersionEnvelope(CreateForVersion(command, commandType, dueUtc, expiresAtUtc, version, recurring), exception);
        }
    }

    [RequiresUnreferencedCode("Uses MakeGenericType and Activator.CreateInstance for envelope construction on the reflection fallback path.")]
    [RequiresDynamicCode("Uses MakeGenericType and Activator.CreateInstance for envelope construction on the reflection fallback path.")]
    private static IDeferredEnvelope CreateForVersion(object command, Type commandType, DateTimeOffset dueUtc, DateTimeOffset? expiresAtUtc, int version, RecurringEnvelopeMetadata? recurring) {
        if(recurring is null) {
            return Create(command, commandType, dueUtc, expiresAtUtc, version);
        }

        try {
            var envelopeType = typeof(RecurringEnvelope<>).MakeGenericType(commandType);

            return (IDeferredEnvelope)Activator.CreateInstance(
                envelopeType,
                command,
                CronExpression.Parse(recurring.CronExpression),
                recurring.UniqueKey,
                TimeZoneInfo.FindSystemTimeZoneById(recurring.TimeZoneId),
                recurring.Overlap,
                dueUtc,
                expiresAtUtc,
                version)!;
        }
        catch(Exception exception) when(exception is ArgumentException or InvalidOperationException or InvalidTimeZoneException or TimeZoneNotFoundException) {
            // Corrupt recurrence metadata can never schedule again; the caller drops the ghost.
            throw new InvalidOperationException($"Recurring metadata for '{commandType.Name}' is invalid.", exception);
        }
    }

    [RequiresUnreferencedCode("Uses MakeGenericType and Activator.CreateInstance for envelope construction on the reflection fallback path.")]
    [RequiresDynamicCode("Uses MakeGenericType and Activator.CreateInstance for envelope construction on the reflection fallback path.")]
    private static IDeferredEnvelope Create(object command, Type commandType, DateTimeOffset dueUtc, DateTimeOffset? expiresAtUtc, int version) {
        // ICommand inherits ICommand<Unit>, so probing ICommand<> first would rebuild every
        // void command as a result envelope and lose the one-generic ICommandHandler<T> form.
        var resultInterface = typeof(ICommand).IsAssignableFrom(commandType)
            ? null
            : commandType.GetInterfaces().FirstOrDefault(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
        var envelopeType = resultInterface is null
            ? typeof(DeferredEnvelope<>).MakeGenericType(commandType)
            : typeof(DeferredResultEnvelope<,>).MakeGenericType(commandType, resultInterface.GetGenericArguments()[0]);

        return (IDeferredEnvelope)Activator.CreateInstance(envelopeType, command, dueUtc, expiresAtUtc, version)!;
    }
}
