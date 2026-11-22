//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>
/// Starts every configured rider in registration order and stops them in reverse order, so a
/// downstream consumer drains before the transport feeding it is stopped. Validation runs before
/// any transport starts.
/// </summary>
public sealed class CqrsRiderHost(CqrsRiderRegistry registry, CqrsRiderMetrics metrics) : IHostedService {
    private readonly CqrsRiderRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly CqrsRiderMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    private readonly List<CqrsRider> _started = [];

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken) {
        if(_started.Count > 0) {
            return;
        }

        _registry.Validate();

        foreach(var rider in _registry.Riders) {
            _registry.SetStatus(rider.Name, CqrsRiderStatus.Starting);
            _started.Add(rider);

            try {
                await rider.Transport.StartAsync(cancellationToken).ConfigureAwait(false);
                _registry.SetStatus(rider.Name, CqrsRiderStatus.Started);
                _metrics.RecordStarted(rider);
            }
            catch(Exception) {
                _registry.SetStatus(rider.Name, CqrsRiderStatus.Faulted);
                _metrics.RecordFault(rider);
                await StopStartedAsync(CancellationToken.None).ConfigureAwait(false);

                throw;
            }
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken) {
        var failures = new List<Exception>();

        for(var index = _started.Count - 1; index >= 0; index--) {
            cancellationToken.ThrowIfCancellationRequested();

            var rider = _started[index];
            _registry.SetStatus(rider.Name, CqrsRiderStatus.Stopping);

            try {
                await rider.Transport.StopAsync(cancellationToken).ConfigureAwait(false);
                _registry.SetStatus(rider.Name, CqrsRiderStatus.Stopped);
                _metrics.RecordStopped(rider);
            }
            catch(Exception exception) {
                _registry.SetStatus(rider.Name, CqrsRiderStatus.Faulted);
                _metrics.RecordFault(rider);
                failures.Add(exception);
            }
        }

        _started.Clear();

        if(failures.Count > 0) {
            throw new AggregateException("One or more riders failed to stop.", failures);
        }
    }

    private async Task StopStartedAsync(CancellationToken cancellationToken) {
        for(var index = _started.Count - 1; index >= 0; index--) {
            var rider = _started[index];
            var alreadyFaulted = rider.Status is CqrsRiderStatus.Faulted;

            if(!alreadyFaulted) {
                _registry.SetStatus(rider.Name, CqrsRiderStatus.Stopping);
            }

            try {
                await rider.Transport.StopAsync(cancellationToken).ConfigureAwait(false);

                if(!alreadyFaulted) {
                    _registry.SetStatus(rider.Name, CqrsRiderStatus.Stopped);
                    _metrics.RecordStopped(rider);
                }
            }
            catch(Exception) {
                _registry.SetStatus(rider.Name, CqrsRiderStatus.Faulted);
                _metrics.RecordFault(rider);
            }
        }

        _started.Clear();
    }
}