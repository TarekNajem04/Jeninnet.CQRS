//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Notifications;

/// <summary>
/// Default <see cref="INotificationPublisher"/>: resolves handlers from DI per publish, delivers
/// according to <see cref="NotificationPublishOptions.Strategy"/>, then notifies process-wide
/// <see cref="INotificationObserver"/> implementations.
/// </summary>
/// <remarks>
/// Initializes a new instance with the given service provider and options.
/// </remarks>
/// <param name="provider">The provider used to resolve handlers for each notification.</param>
/// <param name="options">The publishing options.</param>
/// <param name="observers">Process-wide observers such as the saga dispatcher.</param>
public sealed class DefaultNotificationPublisher(
    IServiceProvider provider,
    IOptions<NotificationPublishOptions> options,
    IEnumerable<INotificationObserver> observers) : IDirectNotificationPublisher {
    private readonly IServiceProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    private readonly NotificationPublishOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    private readonly INotificationObserver[] _observers = observers?.ToArray() ?? [];

    /// <inheritdoc />
    public async Task PublishAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : Interfaces.INotification {
        Guard.NotNull(notification);

        var handlers = _provider.GetServices<Interfaces.INotificationHandler<TNotification>>().ToList();

        if(handlers.Count > 0) {
            await (_options.Strategy switch {
                NotificationPublishStrategy.Parallel => PublishParallelAsync(handlers, notification, cancellationToken),
                _ => PublishSequentialAsync(handlers, notification, cancellationToken)
            }).ConfigureAwait(false);
        }

        var notificationType = notification.GetType();

        foreach(var observer in _observers) {
            await observer.OnPublishingAsync(notification, notificationType, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task PublishSequentialAsync<TNotification>(
        List<Interfaces.INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : Interfaces.INotification {
        // Fail-fast: the first exception propagates and later handlers never run.
        foreach(var handler in handlers) {
            await handler.HandleAsync(notification, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task PublishParallelAsync<TNotification>(
        List<Interfaces.INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : Interfaces.INotification {
        // Every handler is started; Task.WhenAll surfaces the first exception.
        var pending = new Task[handlers.Count];

        for(var i = 0; i < handlers.Count; i++) {
            pending[i] = handlers[i].HandleAsync(notification, cancellationToken);
        }

        return Task.WhenAll(pending);
    }
}