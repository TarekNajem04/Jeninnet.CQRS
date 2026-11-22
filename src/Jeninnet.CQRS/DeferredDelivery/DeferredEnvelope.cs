//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.DeferredDelivery;

/// <summary>
/// A deferred void command. The generic dispatch is closed over the concrete command
/// type at construction time, so recovery can rebuild it without reflection in this
/// assembly (reconstruction lives in the EF Core package).
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
public sealed class DeferredEnvelope<TCommand>(TCommand command, DateTimeOffset dueUtc, DateTimeOffset? expiresAtUtc, int version = 1) : IDeferredEnvelope
    where TCommand : ICommand {
    /// <summary>Gets the command.</summary>
    public TCommand Command { get; } = command ?? throw new ArgumentNullException(nameof(command));

    /// <inheritdoc />
    public int Version { get; } = version < 1 ? 1 : version;

    /// <inheritdoc />
    public DateTimeOffset DueUtc { get; } = dueUtc;

    /// <inheritdoc />
    public DateTimeOffset? ExpiresAtUtc { get; } = expiresAtUtc;

    /// <inheritdoc />
    public string? TenantId { get; init; }

    /// <inheritdoc />
    object IDeferredEnvelope.Command => Command;

    /// <inheritdoc />
    public IDeferredEnvelope WithTenant(string? tenantId) =>
        new DeferredEnvelope<TCommand>(Command, DueUtc, ExpiresAtUtc, Version) { TenantId = tenantId };

    /// <inheritdoc />
    public Task DispatchAsync(IServiceProvider services, CancellationToken cancellationToken) {
        Guard.NotNull(services);

        return services.GetRequiredService<ICqrsBroker>().DispatchAsync(Command, cancellationToken);
    }
}

/// <summary>
/// A deferred command with a discarded result. Fire-and-forget by contract.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResult">The (discarded) result type.</typeparam>
public sealed class DeferredResultEnvelope<TCommand, TResult>(TCommand command, DateTimeOffset dueUtc, DateTimeOffset? expiresAtUtc, int version = 1) : IDeferredEnvelope
    where TCommand : ICommand<TResult> {
    /// <summary>Gets the command.</summary>
    public TCommand Command { get; } = command ?? throw new ArgumentNullException(nameof(command));

    /// <inheritdoc />
    public int Version { get; } = version < 1 ? 1 : version;

    /// <inheritdoc />
    public DateTimeOffset DueUtc { get; } = dueUtc;

    /// <inheritdoc />
    public DateTimeOffset? ExpiresAtUtc { get; } = expiresAtUtc;

    /// <inheritdoc />
    public string? TenantId { get; init; }

    /// <inheritdoc />
    object IDeferredEnvelope.Command => Command;

    /// <inheritdoc />
    public IDeferredEnvelope WithTenant(string? tenantId) =>
        new DeferredResultEnvelope<TCommand, TResult>(Command, DueUtc, ExpiresAtUtc, Version) { TenantId = tenantId };

    /// <inheritdoc />
    public async Task DispatchAsync(IServiceProvider services, CancellationToken cancellationToken) {
        Guard.NotNull(services);

        await services.GetRequiredService<ICqrsBroker>().DispatchAsync<TCommand, TResult>(Command, cancellationToken).ConfigureAwait(false);
    }
}
