//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

[TestClass]
public class StreamingTests {
    private static IQueryDispatcher Dispatcher<TQuery, TItem>(IStreamQueryHandler<TQuery, TItem> handler)
        where TQuery : IStreamQuery<TItem> {
        var services = new ServiceCollection();
        services.AddSingleton(handler);
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();

        return services.BuildServiceProvider().GetRequiredService<IQueryDispatcher>();
    }

    [TestMethod]
    public async Task StreamAsync_YieldsLazilyAsync() {
        // Arrange
        var produced = new List<int>();
        var dispatcher = Dispatcher(new CountingHandler(produced));

        // Act: take one item, then stop.
        var consumed = new List<int>();
        await foreach(var item in dispatcher.StreamAsync<NumbersQuery, int>(new NumbersQuery(Count: 5), default)) {
            consumed.Add(item);
            break;
        }

        // Assert: the producer ran only up to the first yield.
        MSTestAssertions.That(() => consumed.Should().Equal([1]));
        MSTestAssertions.That(() => produced.Should().Equal([1]));
    }

    [TestMethod]
    public async Task StreamAsync_EmptyStream_CompletesAsync() {
        // Arrange
        var dispatcher = Dispatcher<NumbersQuery, int>(new EmptyHandler());

        // Act
        var consumed = new List<int>();
        await foreach(var item in dispatcher.StreamAsync<NumbersQuery, int>(new NumbersQuery(Count: 0), default)) {
            consumed.Add(item);
        }

        // Assert
        MSTestAssertions.That(() => consumed.Should().BeEmpty());
    }

    [TestMethod]
    public async Task StreamAsync_ThrowingStream_SurfacesAfterFirstItemAsync() {
        // Arrange
        var dispatcher = Dispatcher<NumbersQuery, int>(new ThrowingHandler());

        // Act
        var consumed = new List<int>();
        InvalidOperationException? thrown = null;
        try {
            await foreach(var item in dispatcher.StreamAsync<NumbersQuery, int>(new NumbersQuery(Count: 3), default)) {
                consumed.Add(item);
            }
        }
        catch(InvalidOperationException ex) {
            thrown = ex;
        }

        // Assert
        MSTestAssertions.That(() => consumed.Should().Equal([1]));
        MSTestAssertions.That(() => thrown.Should().NotBeNull());
        MSTestAssertions.That(() => thrown!.Message.Should().Be("mid"));
    }

    [TestMethod]
    public async Task StreamAsync_ObservesPreCancelledTokenAsync() {
        // Arrange
        var dispatcher = Dispatcher<NumbersQuery, int>(new CancellingHandler());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        OperationCanceledException? thrown = null;
        try {
            await foreach(var _ in dispatcher.StreamAsync<NumbersQuery, int>(new NumbersQuery(Count: 3), cts.Token)) {
            }
        }
        catch(OperationCanceledException ex) {
            thrown = ex;
        }

        // Assert
        MSTestAssertions.That(() => thrown.Should().NotBeNull());
    }

    [TestMethod]
    public void StreamAsync_WithoutHandler_ThrowsWithQueryName() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        var dispatcher = services.BuildServiceProvider().GetRequiredService<IQueryDispatcher>();

        // Act
        InvalidOperationException? thrown = null;
        try {
            _ = dispatcher.StreamAsync<NumbersQuery, int>(new NumbersQuery(Count: 1), default);
        }
        catch(InvalidOperationException exception) {
            thrown = exception;
        }

        // Assert
        MSTestAssertions.That(() => thrown.Should().NotBeNull());
        MSTestAssertions.That(() => thrown!.Message.Should().Contain(nameof(NumbersQuery)));
    }

    [TestMethod]
    public async Task Broker_DelegatesStreaming_ToQueryDispatcherAsync() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddTransient<IStreamQueryHandler<NumbersQuery, int>, RangeHandler>();
        var broker = services.BuildServiceProvider().GetRequiredService<ICqrsBroker>();

        // Act
        var consumed = new List<int>();
        await foreach(var item in ((IQueryDispatcher)broker).StreamAsync<NumbersQuery, int>(new NumbersQuery(Count: 2), default)) {
            consumed.Add(item);
        }

        // Assert
        MSTestAssertions.That(() => consumed.Should().Equal([1, 2]));
    }

    private sealed record NumbersQuery(int Count) : IStreamQuery<int>;

    private sealed class CountingHandler(List<int> produced) : IStreamQueryHandler<NumbersQuery, int> {
        public async IAsyncEnumerable<int> HandleAsync(NumbersQuery query, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            for(var i = 1; i <= query.Count; i++) {
                produced.Add(i);
                yield return i;
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }
    }

    private sealed class EmptyHandler : IStreamQueryHandler<NumbersQuery, int> {
        public IAsyncEnumerable<int> HandleAsync(NumbersQuery query, CancellationToken cancellationToken) => AsyncEnumerable.Empty<int>();
    }

    private sealed class ThrowingHandler : IStreamQueryHandler<NumbersQuery, int> {
        public async IAsyncEnumerable<int> HandleAsync(NumbersQuery query, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            yield return 1;
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("mid");
        }
    }

    private sealed class CancellingHandler : IStreamQueryHandler<NumbersQuery, int> {
        public async IAsyncEnumerable<int> HandleAsync(NumbersQuery query, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask.ConfigureAwait(false);
            yield return 1;
        }
    }

    private sealed class RangeHandler : IStreamQueryHandler<NumbersQuery, int> {
        public async IAsyncEnumerable<int> HandleAsync(NumbersQuery query, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            for(var i = 1; i <= query.Count; i++) {
                yield return i;
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }
    }
}