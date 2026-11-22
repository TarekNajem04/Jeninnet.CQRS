//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Recurring;

/// <summary>
/// A recurring command staged in the durable deferred store. Dispatch first stages the next
/// occurrence (so a crash never stops the series), then obeys the overlap policy, then runs the
/// command through the normal broker.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
public sealed class RecurringEnvelope<TCommand> : IRecurringEnvelope
    where TCommand : ICommand {
    /// <summary>
    /// Initializes a recurring envelope.
    /// </summary>
    /// <param name="command">The command to run.</param>
    /// <param name="cron">The five-field cron expression.</param>
    /// <param name="uniqueKey">The stable schedule key.</param>
    /// <param name="timeZone">The cron time zone.</param>
    /// <param name="overlap">The overlap policy.</param>
    /// <param name="dueUtc">The current occurrence.</param>
    /// <param name="expiresAtUtc">Optional expiry; recurring envelopes normally never expire.</param>
    /// <param name="version">The message schema version.</param>
    public RecurringEnvelope( // NOSONAR: Public options/records intentionally group related settings; splitting would fragment the API.
        TCommand command,
        CronExpression cron,
        string uniqueKey,
        TimeZoneInfo timeZone,
        RecurringOverlapPolicy overlap,
        DateTimeOffset dueUtc,
        DateTimeOffset? expiresAtUtc = null,
        int version = 1) {
        Command = command ?? throw new ArgumentNullException(nameof(command));
        Cron = cron ?? throw new ArgumentNullException(nameof(cron));
        UniqueKey = uniqueKey ?? throw new ArgumentNullException(nameof(uniqueKey));
        TimeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));
        Overlap = overlap;
        DueUtc = dueUtc;
        ExpiresAtUtc = expiresAtUtc;
        Version = version < 1 ? 1 : version;
    }

    /// <summary>Gets the command instance.</summary>
    public TCommand Command { get; }

    /// <inheritdoc />
    object IDeferredEnvelope.Command => Command;

    /// <inheritdoc />
    public string UniqueKey { get; }

    /// <inheritdoc />
    public string Expression => Cron.Expression;

    /// <inheritdoc />
    public CronExpression Cron { get; }

    /// <inheritdoc />
    public TimeZoneInfo TimeZone { get; }

    /// <inheritdoc />
    public RecurringOverlapPolicy Overlap { get; }

    /// <inheritdoc />
    public DateTimeOffset DueUtc { get; }

    /// <inheritdoc />
    public DateTimeOffset? ExpiresAtUtc { get; }

    /// <inheritdoc />
    public int Version { get; }

    /// <inheritdoc />
    public string? TenantId { get; init; }

    /// <inheritdoc />
    public IDeferredEnvelope WithTenant(string? tenantId) =>
        new RecurringEnvelope<TCommand>(Command, Cron, UniqueKey, TimeZone, Overlap, DueUtc, ExpiresAtUtc, Version) { TenantId = tenantId };

    /// <inheritdoc />
    public IDeferredEnvelope CreateNext(DateTimeOffset dueUtc) =>
        new RecurringEnvelope<TCommand>(Command, Cron, UniqueKey, TimeZone, Overlap, dueUtc, expiresAtUtc: null, Version) { TenantId = TenantId };

    /// <inheritdoc />
    public async Task DispatchAsync(IServiceProvider services, CancellationToken cancellationToken) {
        Guard.NotNull(services);

        var clock = services.GetRequiredService<TimeProvider>();
        var scheduler = services.GetRequiredService<IRecurringScheduler>();

        // Stage the next occurrence before running: a crash preserves the series.
        await scheduler.StageNextAsync(this, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);

        var gate = services.GetRequiredService<RecurringExecutionGate>();
        using var lease = await gate.EnterAsync(UniqueKey, Overlap, cancellationToken).ConfigureAwait(false);

        if(lease is null) {
            return; // Skip policy: this occurrence is superseded by the running one.
        }

        await services.GetRequiredService<ICqrsBroker>()
            .DispatchAsync(Command, cancellationToken)
            .ConfigureAwait(false);
    }
}
