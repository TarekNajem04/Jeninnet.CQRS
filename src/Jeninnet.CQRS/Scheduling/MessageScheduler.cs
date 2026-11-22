//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Scheduling;

/// <summary>Default <see cref="IMessageScheduler"/> backed by <see cref="ISchedulerStore"/>.</summary>
/// <param name="store">The persistent schedule store.</param>
/// <param name="serializer">The payload serializer.</param>
/// <param name="clock">The clock used for due instants.</param>
/// <param name="tenant">The ambient tenant adapter; <see langword="null"/> without tenancy.</param>
public sealed class MessageScheduler(ISchedulerStore store, IMessageSerializer serializer, TimeProvider clock, TenantAccessor? tenant = null) : IMessageScheduler {
    private readonly ISchedulerStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IMessageSerializer _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly TenantAccessor? _tenant = tenant;

    /// <inheritdoc />
    public Task<string> ScheduleAsync<TMessage>(TMessage message, TimeSpan delay, ScheduledMessageOptions? options = null, CancellationToken cancellationToken = default) {
        Guard.NotNull(message);

        var due = _clock.GetUtcNow() + (delay < TimeSpan.Zero ? TimeSpan.Zero : delay);

        return ScheduleAtAsync(message, due, options, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> ScheduleAtAsync<TMessage>(TMessage message, DateTimeOffset dueUtc, ScheduledMessageOptions? options = null, CancellationToken cancellationToken = default) {
        Guard.NotNull(message);

        options ??= new ScheduledMessageOptions();
        var key = StringCompat.IsNullOrWhiteSpace(options.Key) ? Guid.NewGuid().ToString("N") : options.Key.Trim();
        var scheduled = Create(message, key, ScheduledMessageKind.OneShot, dueUtc, null, options.TimeZone);
        await _store.TryAddAsync(scheduled, cancellationToken).ConfigureAwait(false);

        return key;
    }

    /// <inheritdoc />
    public async Task<string> ScheduleRecurringAsync<TMessage>(TMessage message, CronExpression cron, string key, ScheduledMessageOptions? options = null, CancellationToken cancellationToken = default) {
        Guard.NotNull(message);
        Guard.NotNull(cron);
        Guard.NotNullOrWhiteSpace(key);

        options ??= new ScheduledMessageOptions();
        var due = cron.GetNextOccurrence(_clock.GetUtcNow(), options.TimeZone);
        var scheduled = Create(message, key.Trim(), ScheduledMessageKind.Recurring, due, cron.Expression, options.TimeZone);
        await _store.TryAddAsync(scheduled, cancellationToken).ConfigureAwait(false);

        return key.Trim();
    }

    /// <inheritdoc />
    public Task<bool> CancelAsync(string key, CancellationToken cancellationToken = default) {
        // Enforce strict mode before touching a tenant-owned key the caller cannot name.
        _ = _tenant?.Current;

        return _store.CancelAsync(key, cancellationToken);
    }

    private ScheduledMessage Create<TMessage>(TMessage message, string key, ScheduledMessageKind kind, DateTimeOffset dueUtc, string? cron, TimeZoneInfo timeZone) {
        var messageType = typeof(TMessage);
        var payload = _serializer.Serialize(message!, messageType);

        return new ScheduledMessage(
            key,
            messageType.AssemblyQualifiedName ?? messageType.FullName ?? messageType.Name,
            _serializer.ContentType,
            payload,
            kind,
            dueUtc,
            _clock.GetUtcNow(),
            cron,
            kind is ScheduledMessageKind.Recurring ? timeZone.Id : null,
            0,
            0,
            _tenant?.Current);
    }
}
