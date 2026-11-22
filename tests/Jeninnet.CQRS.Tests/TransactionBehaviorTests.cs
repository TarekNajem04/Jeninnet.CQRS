//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

[TestClass]
public class TransactionBehaviorTests {
    [TestMethod]
    public async Task HandleAsync_ShouldBeginAndCommit_OnSuccessAsync() {
        // Arrange
        var scope = new RecordingTransactionScope();
        var behavior = new TransactionBehavior<TestCommand, string>(scope);

        static Task<string> next(TestCommand _, CancellationToken __) => Task.FromResult("ok");

        // Act
        var result = await behavior.HandleAsync(new TestCommand("Run"), next, default);

        // Assert
        MSTestAssertions.That(() => result.Should().Be("ok"));
        MSTestAssertions.That(() => scope.Calls.Should().Equal(["Begin", "Commit"]));
    }

    [TestMethod]
    public async Task HandleAsync_ShouldRollback_AndRethrow_OnFailureAsync() {
        // Arrange
        var scope = new RecordingTransactionScope();
        var behavior = new TransactionBehavior<TestCommand, string>(scope);
        var boom = new InvalidOperationException("boom");

        Task<string> next(TestCommand _, CancellationToken __) => Task.FromException<string>(boom);

        // Act
        InvalidOperationException? thrown = null;
        try {
            await behavior.HandleAsync(new TestCommand("Run"), next, default);
        }
        catch(InvalidOperationException ex) {
            thrown = ex;
        }

        // Assert
        MSTestAssertions.That(() => thrown.Should().NotBeNull());
        MSTestAssertions.That(() => ReferenceEquals(thrown, boom).Should().BeTrue());
        MSTestAssertions.That(() => scope.Calls.Should().Equal(["Begin", "Rollback"]));
    }

    [TestMethod]
    public async Task HandleAsync_ShouldPreserveOriginal_WhenRollbackThrowsAsync() {
        // Arrange
        var scope = new RecordingTransactionScope { FailRollback = true };
        var behavior = new TransactionBehavior<TestCommand, string>(scope);
        var boom = new InvalidOperationException("boom");

        Task<string> next(TestCommand _, CancellationToken __) => Task.FromException<string>(boom);

        // Act
        InvalidOperationException? thrown = null;
        try {
            await behavior.HandleAsync(new TestCommand("Run"), next, default);
        }
        catch(InvalidOperationException ex) {
            thrown = ex;
        }

        // Assert
        MSTestAssertions.That(() => ReferenceEquals(thrown, boom).Should().BeTrue());
        MSTestAssertions.That(() => scope.Calls.Should().Equal(["Begin", "Rollback"]));
    }

    [TestMethod]
    public async Task AddCqrsTransactionBehavior_ShouldResolveWorkingBehaviorAsync() {
        // Arrange
        var services = new ServiceCollection();
        services.AddCqrsTransactionBehavior();
        var provider = services.BuildServiceProvider();

        // Act
        var behavior = provider.GetServices<IPipelineBehavior<TestCommand, string>>().Single();

        static Task<string> next(TestCommand _, CancellationToken __) => Task.FromResult("ok");
        var result = await behavior.HandleAsync(new TestCommand("Run"), next, default);

        // Assert: the default no-op scope keeps the pipeline honest.
        MSTestAssertions.That(() => result.Should().Be("ok"));
    }

    [TestMethod]
    public void AddCqrsTransactionBehavior_ShouldPrefer_UserScope() {
        // Arrange
        var services = new ServiceCollection();
        services.AddCqrsTransactionBehavior();
        services.AddTransient<ITransactionScope, RecordingTransactionScope>();
        var provider = services.BuildServiceProvider();

        // Act
        var scope = provider.GetRequiredService<ITransactionScope>();

        // Assert: the last registration wins over the no-op default.
        MSTestAssertions.That(() => scope.Should().BeOfType<RecordingTransactionScope>());
    }

    private sealed record TestCommand(string Message) : ICommand<string>;

    private sealed class RecordingTransactionScope : ITransactionScope {
        public readonly List<string> Calls = [];

        public bool FailRollback { get; set; }

        public Task BeginAsync(CancellationToken cancellationToken) {
            Calls.Add("Begin");
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken cancellationToken) {
            Calls.Add("Commit");
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken) {
            Calls.Add("Rollback");

            if(FailRollback) {
                throw new InvalidOperationException("rollback failed");
            }

            return Task.CompletedTask;
        }
    }
}