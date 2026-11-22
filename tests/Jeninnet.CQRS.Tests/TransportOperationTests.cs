//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// The shared transport send/publish wrapper: observer ordering, fault reporting, and cancellation
/// behavior must be identical for every broker.
/// </summary>
[TestClass]
public sealed class TransportOperationTests {
    private static TransportEnvelope CreateEnvelope() =>
        new(
            "message-1",
            typeof(string).FullName!,
            "application/json",
            new byte[] { 1, 2, 3 },
            null,
            null,
            null,
            null,
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>(StringComparer.Ordinal),
            new TransportTopology("exchange", "queue", "topic", "route"),
            TransportDeliveryMode.Send);

    [TestMethod]
    public async Task SendAsync_WithoutObservers_InvokesTheBrokerOperation() {
        var calls = 0;

        await TransportOperation.SendAsync(
            observers: null,
            "message",
            typeof(string),
            CreateEnvelope(),
            _ => {
                calls++;
                return Task.CompletedTask;
            },
            CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task SendAsync_WithObservers_ReportsPreAndPostInOrder() {
        var observer = new RecordingSendObserver();

        await TransportOperation.SendAsync(
            new MessageObserverInvoker(send: [observer]),
            "message",
            typeof(string),
            CreateEnvelope(),
            _ => Task.CompletedTask,
            CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(1, observer.Sending);
        Assert.AreEqual(1, observer.Sent);
        Assert.AreEqual(0, observer.Fault);
    }

    [TestMethod]
    public async Task SendAsync_WhenTheBrokerFails_ReportsTheFaultAndRethrows() {
        var observer = new RecordingSendObserver();

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            TransportOperation.SendAsync(
                new MessageObserverInvoker(send: [observer]),
                "message",
                typeof(string),
                CreateEnvelope(),
                _ => throw new InvalidOperationException("broker failed"),
                CancellationToken.None)).ConfigureAwait(false);

        Assert.AreEqual("broker failed", error.Message);
        Assert.AreEqual(1, observer.Fault);
        Assert.AreEqual(0, observer.Sent);
    }

    [TestMethod]
    public async Task SendAsync_WhenCanceled_DoesNotReportAFault() {
        var observer = new RecordingSendObserver();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            TransportOperation.SendAsync(
                new MessageObserverInvoker(send: [observer]),
                "message",
                typeof(string),
                CreateEnvelope(),
                ct => throw new OperationCanceledException(ct),
                cancellation.Token)).ConfigureAwait(false);

        Assert.AreEqual(0, observer.Fault);
    }

    [TestMethod]
    public async Task PublishAsync_WithObservers_ReportsPreAndPostInOrder() {
        var observer = new RecordingPublishObserver();

        await TransportOperation.PublishAsync(
            new MessageObserverInvoker(publish: [observer]),
            "message",
            typeof(string),
            CreateEnvelope(),
            _ => Task.CompletedTask,
            CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(1, observer.Publishing);
        Assert.AreEqual(1, observer.Published);
        Assert.AreEqual(0, observer.Fault);
    }

    [TestMethod]
    public async Task PublishAsync_WhenTheBrokerFails_ReportsTheFaultAndRethrows() {
        var observer = new RecordingPublishObserver();

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            TransportOperation.PublishAsync(
                new MessageObserverInvoker(publish: [observer]),
                "message",
                typeof(string),
                CreateEnvelope(),
                _ => throw new InvalidOperationException("broker failed"),
                CancellationToken.None)).ConfigureAwait(false);

        Assert.AreEqual("broker failed", error.Message);
        Assert.AreEqual(1, observer.Fault);
        Assert.AreEqual(0, observer.Published);
    }

    private sealed class RecordingSendObserver : ISendObserver {
        public int Sending { get; private set; }
        public int Sent { get; private set; }
        public int Fault { get; private set; }

        public Task OnSendingAsync(OutboundObservationContext context, CancellationToken cancellationToken = default) {
            Sending++;
            return Task.CompletedTask;
        }

        public Task OnSentAsync(OutboundObservationContext context, CancellationToken cancellationToken = default) {
            Sent++;
            return Task.CompletedTask;
        }

        public Task OnSendFaultAsync(OutboundObservationContext context, Exception error, CancellationToken cancellationToken = default) {
            Fault++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPublishObserver : IPublishObserver {
        public int Publishing { get; private set; }
        public int Published { get; private set; }
        public int Fault { get; private set; }

        public Task OnPublishingAsync(OutboundObservationContext context, CancellationToken cancellationToken = default) {
            Publishing++;
            return Task.CompletedTask;
        }

        public Task OnPublishedAsync(OutboundObservationContext context, CancellationToken cancellationToken = default) {
            Published++;
            return Task.CompletedTask;
        }

        public Task OnPublishFaultAsync(OutboundObservationContext context, Exception error, CancellationToken cancellationToken = default) {
            Fault++;
            return Task.CompletedTask;
        }
    }
}
