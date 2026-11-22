//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.DeferredDelivery;

/// <summary>
/// A durable row whose command type neither the generated registry nor the reflection fallback
/// can resolve. Dispatch throws the descriptive failure, so the pump dead-letters the row instead
/// of silently dropping a message that was already accepted.
/// </summary>
/// <param name="commandTypeName">The stored type name that could not be resolved.</param>
/// <param name="dueUtc">When the command became due.</param>
/// <param name="expiresAtUtc">When the entry expires, if ever.</param>
/// <param name="version">The stored message schema version.</param>
public sealed class UnresolvedCommandEnvelope(string commandTypeName, DateTimeOffset dueUtc, DateTimeOffset? expiresAtUtc, int version = 1) : IDeferredEnvelope {
    /// <summary>Gets the stored type name that could not be resolved.</summary>
    public string CommandTypeName { get; } = commandTypeName ?? throw new ArgumentNullException(nameof(commandTypeName));

    /// <summary>
    /// Gets the stored type name as a placeholder so fault reporting (which reads
    /// <see cref="object.GetType"/>) cannot throw while handling the original failure.
    /// </summary>
    public object Command => CommandTypeName;

    /// <inheritdoc />
    public int Version { get; } = version < 1 ? 1 : version;

    /// <inheritdoc />
    public DateTimeOffset DueUtc { get; } = dueUtc;

    /// <inheritdoc />
    public DateTimeOffset? ExpiresAtUtc { get; } = expiresAtUtc;

    /// <inheritdoc />
    public string? TenantId { get; init; }

    /// <inheritdoc />
    public IDeferredEnvelope WithTenant(string? tenantId) =>
        new UnresolvedCommandEnvelope(CommandTypeName, DueUtc, ExpiresAtUtc, Version) { TenantId = tenantId };

    /// <inheritdoc />
    public Task DispatchAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            $"The deferred command type '{CommandTypeName}' could not be resolved. Declare the command in the application assembly so the generated registry covers it.");
}