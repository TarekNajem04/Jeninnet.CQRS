//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Analyzers.Tests;

[TestClass]
public class MisuseAnalyzerTests {
    private const string HEADER =
        """
        #nullable enable
        using System.Threading;
        using System.Threading.Tasks;
        using Jeninnet.CQRS.Interfaces;

        """;

    private static Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string body) =>
        AnalyzerTestHost.GetAnalyzerDiagnosticsAsync(HEADER + body);

    private static void ShouldHaveSingle(IReadOnlyList<Diagnostic> diagnostics, string id) {
        var matching = diagnostics.Where(diagnostic => diagnostic.Id == id).ToList();
        MSTestAssertions.That(() => matching.Should().HaveCount(1));
    }

    [TestMethod]
    public async Task DuplicateHandler_Fires_OnSecondHandlerAsync() {
        // Arrange
        const string source =
            """
            public sealed record Ping(string Name) : ICommand<string>;
            public sealed class First : ICommandHandler<Ping, string> {
                public Task<string> HandleAsync(Ping c, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult("a"); }
            }
            public sealed class Second : ICommandHandler<Ping, string> {
                public Task<string> HandleAsync(Ping c, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult("b"); }
            }
            """;

        // Act
        var diagnostics = await AnalyzeAsync(source);

        // Assert
        ShouldHaveSingle(diagnostics, "CQRS0001");
    }

    [TestMethod]
    public async Task DuplicateHandler_Silent_ForDistinctMessagesAsync() {
        // Arrange
        const string source =
            """
            public sealed record Ping(string Name) : ICommand<string>;
            public sealed record Pong(string Name) : ICommand<string>;
            public sealed class First : ICommandHandler<Ping, string> {
                public Task<string> HandleAsync(Ping c, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult("a"); }
            }
            public sealed class Second : ICommandHandler<Pong, string> {
                public Task<string> HandleAsync(Pong c, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult("b"); }
            }
            """;

        // Act
        var diagnostics = await AnalyzeAsync(source);

        // Assert
        MSTestAssertions.That(() => diagnostics.Should().BeEmpty());
    }

    [TestMethod]
    public async Task DuplicateHandler_Silent_ForSingleHandlerAsync() {
        // Arrange
        const string source =
            """
            public sealed record Ping(string Name) : ICommand<string>;
            public sealed class Only : ICommandHandler<Ping, string> {
                public Task<string> HandleAsync(Ping c, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult("a"); }
            }
            """;

        // Act
        var diagnostics = await AnalyzeAsync(source);

        // Assert
        MSTestAssertions.That(() => diagnostics.Should().BeEmpty());
    }

    [TestMethod]
    public async Task UnusedCancellationToken_Fires_WhenIgnoredAsync() {
        // Arrange
        const string source =
            """
            public sealed record Ping(string Name) : ICommand<string>;
            public sealed class Ignoring : ICommandHandler<Ping, string> {
                public Task<string> HandleAsync(Ping c, CancellationToken ct) => Task.FromResult("a");
            }
            """;

        // Act
        var diagnostics = await AnalyzeAsync(source);

        // Assert
        ShouldHaveSingle(diagnostics, "CQRS0002");
    }

    [TestMethod]
    public async Task UnusedCancellationToken_Silent_WhenForwardedAsync() {
        // Arrange
        const string source =
            """
            public sealed record Ping(string Name) : ICommand<string>;
            public sealed class Forwarding : ICommandHandler<Ping, string> {
                public async Task<string> HandleAsync(Ping c, CancellationToken ct) {
                    await Task.Delay(1, ct).ConfigureAwait(false);
                    return "a";
                }
            }
            """;

        // Act
        var diagnostics = await AnalyzeAsync(source);

        // Assert
        MSTestAssertions.That(() => diagnostics.Should().BeEmpty());
    }

    [TestMethod]
    public async Task UnusedCancellationToken_Silent_ForDiscardNameAsync() {
        // Arrange: an explicit discard opts out of the check.
        const string source =
            """
            public sealed record Ping(string Name) : ICommand<string>;
            public sealed class Discarding : ICommandHandler<Ping, string> {
                public Task<string> HandleAsync(Ping c, CancellationToken _) => Task.FromResult("a");
            }
            """;

        // Act
        var diagnostics = await AnalyzeAsync(source);

        // Assert
        MSTestAssertions.That(() => diagnostics.Should().BeEmpty());
    }

    [TestMethod]
    public async Task MultipleMessages_Fires_ForTwoContractsAsync() {
        // Arrange
        const string source =
            """
            public sealed record A(string N) : ICommand<string>;
            public sealed record E(string N) : INotification;
            public sealed class Both : ICommandHandler<A, string>, INotificationHandler<E> {
                public Task<string> HandleAsync(A c, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult("a"); }
                public Task HandleAsync(E e, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
            }
            """;

        // Act
        var diagnostics = await AnalyzeAsync(source);

        // Assert
        ShouldHaveSingle(diagnostics, "CQRS0003");
    }

    [TestMethod]
    public async Task MultipleMessages_Silent_ForSingleContractAsync() {
        // Arrange
        const string source =
            """
            public sealed record A(string N) : ICommand<string>;
            public sealed class Single : ICommandHandler<A, string> {
                public Task<string> HandleAsync(A c, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult("a"); }
            }
            """;

        // Act
        var diagnostics = await AnalyzeAsync(source);

        // Assert
        MSTestAssertions.That(() => diagnostics.Should().BeEmpty());
    }

    [TestMethod]
    public async Task DuplicateHandler_Silent_ForOneParamVoidHandlerAsync() {
        // Arrange: a one-generic void handler implements both ICommandHandler<T> and
        // ICommandHandler<T, Unit> through inheritance; the same class must never
        // count as a duplicate of itself (CQRS-16 generated twin).
        const string source =
            """
            using Jeninnet.CQRS;
            public sealed record Ping(string N) : ICommand;
            public sealed class Only : ICommandHandler<Ping> {
                public Task<Unit> HandleAsync(Ping c, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(Unit.Value); }
            }
            """;

        // Act
        var diagnostics = await AnalyzeAsync(source);

        // Assert
        MSTestAssertions.That(() => diagnostics.Should().BeEmpty());
    }
}
