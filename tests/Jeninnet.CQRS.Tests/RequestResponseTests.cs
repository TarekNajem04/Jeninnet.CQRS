//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>A request payload for the transport round-trip tests.</summary>
/// <param name="Value">The echoed value.</param>
public sealed record EchoRequest(string Value);

/// <summary>A response payload for the transport round-trip tests.</summary>
/// <param name="Value">The echoed value.</param>
public sealed record EchoResponse(string Value);

/// <summary>Responder A for the multi-response test.</summary>
public sealed class EchoResponderA : RequestResponder<EchoRequest, EchoResponse> {
    /// <inheritdoc />
    protected override Task<EchoResponse> RespondAsync(EchoRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new EchoResponse($"{request.Value}-a"));
}

/// <summary>Responder B for the multi-response test.</summary>
public sealed class EchoResponderB : RequestResponder<EchoRequest, EchoResponse> {
    /// <inheritdoc />
    protected override Task<EchoResponse> RespondAsync(EchoRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new EchoResponse($"{request.Value}-b"));
}

/// <summary>
/// Characterization tests for CQRS-50: request/response over the in-memory transport, timeout
/// cleanup, and first/all multi-response behavior.
/// </summary>
[TestClass]
public sealed class RequestResponseTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task RequestResponse_RoundTripsThroughTheTransportAsync() {
        // Arrange
        await using var host = await BuildHostAsync([typeof(EchoResponderA)]);
        var client = host.Provider.GetRequiredService<IRequestClient<EchoRequest, EchoResponse>>();

        // Act
        var response = await client.GetResponseAsync(new EchoRequest("hi"), TimeSpan.FromSeconds(2), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => response.Value.Should().Be("hi-a"));
        var inbox = host.Provider.GetRequiredService<IRequestInbox>();
        MSTestAssertions.That(() => inbox.ListAsync(TestContext!.CancellationToken).GetAwaiter().GetResult().Should().BeEmpty("completed requests leave no pending row"));
    }

    [TestMethod]
    public async Task NoResponder_TimesOut_AndRemovesThePendingRowAsync() {
        // Arrange
        await using var host = await BuildHostAsync([]);
        var client = host.Provider.GetRequiredService<IRequestClient<EchoRequest, EchoResponse>>();
        var inbox = host.Provider.GetRequiredService<IRequestInbox>();

        // Act
        var thrown = await CaptureAsync(() => client.GetResponseAsync(new EchoRequest("missing"), TimeSpan.FromMilliseconds(200), TestContext!.CancellationToken)).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<RequestTimeoutException>());
        MSTestAssertions.That(() => inbox.ListAsync(TestContext!.CancellationToken).GetAwaiter().GetResult().Should().BeEmpty("the timeout path removes the pending row"));
    }

    [TestMethod]
    public async Task First_ReturnsOnce_AndAll_AggregatesBothRespondersAsync() {
        // Arrange
        await using var host = await BuildHostAsync([typeof(EchoResponderA), typeof(EchoResponderB)]);
        var client = host.Provider.GetRequiredService<IRequestClient<EchoRequest, EchoResponse>>();

        // Act
        var first = await client.GetResponseAsync(new EchoRequest("one"), TimeSpan.FromSeconds(2), TestContext!.CancellationToken).ConfigureAwait(false);
        var all = await client.GetAllResponsesAsync(new EchoRequest("two"), TimeSpan.FromMilliseconds(300), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => (first.Value is "one-a" or "one-b").Should().BeTrue());
        MSTestAssertions.That(() => all.Should().HaveCount(2));
        MSTestAssertions.That(() => all.Select(static response => response.Value).Order().Should().Equal(["two-a", "two-b"]));
    }

    [TestMethod]
    public async Task RequestHeaders_AreNotLeakedIntoLaterRequestsAsync() {
        // Arrange: verify the client registers a fresh correlation id for every request.
        await using var host = await BuildHostAsync([typeof(EchoResponderA)]);
        var client = host.Provider.GetRequiredService<IRequestClient<EchoRequest, EchoResponse>>();

        // Act
        var first = await client.GetResponseAsync(new EchoRequest("one"), TimeSpan.FromSeconds(2), TestContext!.CancellationToken).ConfigureAwait(false);
        var second = await client.GetResponseAsync(new EchoRequest("two"), TimeSpan.FromSeconds(2), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => first.Value.Should().Be("one-a"));
        MSTestAssertions.That(() => second.Value.Should().Be("two-a"));
        MSTestAssertions.That(() => host.Provider.GetRequiredService<IRequestInbox>().ListAsync(TestContext!.CancellationToken).GetAwaiter().GetResult().Should().BeEmpty());
    }

    private static async Task<RequestHost> BuildHostAsync(Type[] responderTypes) {
        var services = new ServiceCollection();
        services.AddCqrsInMemoryTransport();
        services.AddCqrsRequestResponse();

        foreach(var responderType in responderTypes) {
            services.AddTransient(typeof(IRequestResponder), responderType);
        }

        var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IMessageTransport>();
        await transport.StartAsync().ConfigureAwait(false);

        return new RequestHost(provider, transport);
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action) {
        try {
            await action().ConfigureAwait(false);

            return null;
        }
        catch(Exception exception) {
            return exception;
        }
    }

    private sealed class RequestHost(ServiceProvider provider, IMessageTransport transport) : IAsyncDisposable {
        internal ServiceProvider Provider { get; } = provider;

        public async ValueTask DisposeAsync() {
            await transport.StopAsync().ConfigureAwait(false);
            await Provider.DisposeAsync().ConfigureAwait(false);
        }
    }
}