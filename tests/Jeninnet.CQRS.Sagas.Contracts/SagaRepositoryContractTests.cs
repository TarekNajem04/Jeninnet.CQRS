//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.Contracts;

/// <summary>
/// Reusable contract every <see cref="ISagaRepository{TState}"/> must satisfy. The in-memory and
/// EF Core suites derive from it; the store implementation is supplied by
/// <see cref="CreateRepository"/>.
/// </summary>
[TestClass]
public abstract class SagaRepositoryContractTests {
    /// <summary>Gets or sets the MSTest context (used for cancellation).</summary>
    public TestContext? TestContext { get; set; }

    private ISagaRepository<SagaRepositoryTestState> _repository = null!;

    /// <summary>Creates a repository over a fresh store for the current test.</summary>
    protected abstract ISagaRepository<SagaRepositoryTestState> CreateRepository();

    /// <summary>Releases any provider or database owned by the derived test.</summary>
    protected virtual ValueTask DisposeRepositoryAsync() => ValueTask.CompletedTask;

    /// <summary>Initializes the per-test repository.</summary>
    [TestInitialize]
    public void Initialize() => _repository = CreateRepository();

    /// <summary>Releases the per-test resources.</summary>
    [TestCleanup]
    public async Task CleanupAsync() => await DisposeRepositoryAsync().ConfigureAwait(false);

    /// <summary>Save/load round-trips and a stale version is rejected.</summary>
    [TestMethod]
    public async Task SaveLoad_RejectsAStaleVersionAsync() {
        // Arrange
        var state = new SagaRepositoryTestState { CurrentState = "waiting", Value = "one" };

        // Act: insert at version zero.
        await _repository.SaveAsync("saga-1", state, 0, SagaProgress.Empty, TestContext!.CancellationToken).ConfigureAwait(false);
        var loaded = await _repository.LoadAsync("saga-1", TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => loaded.Should().NotBeNull());
        var stored = loaded!;
        MSTestAssertions.That(() => stored.Version.Should().Be(1));
        MSTestAssertions.That(() => stored.State.CurrentState.Should().Be("waiting"));
        MSTestAssertions.That(() => stored.State.Value.Should().Be("one"));

        // Act: update with the current version.
        stored.State.CurrentState = "done";
        await _repository.SaveAsync("saga-1", stored.State, stored.Version, SagaProgress.Empty, TestContext!.CancellationToken).ConfigureAwait(false);
        var updated = await _repository.LoadAsync("saga-1", TestContext!.CancellationToken).ConfigureAwait(false);
        var stale = await CaptureAsync(() => _repository.SaveAsync(
            "saga-1",
            state,
            stored.Version,
            SagaProgress.Empty,
            TestContext!.CancellationToken)).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => updated.Should().NotBeNull());
        var refreshed = updated!;
        MSTestAssertions.That(() => refreshed.Version.Should().Be(2));
        MSTestAssertions.That(() => refreshed.State.CurrentState.Should().Be("done"));
        MSTestAssertions.That(() => stale.Should().BeOfType<SagaConcurrencyException>());
    }

    /// <summary>Delete removes the row; a second delete is a no-op.</summary>
    [TestMethod]
    public async Task Delete_RemovesTheRowAsync() {
        // Arrange
        await _repository.SaveAsync(
            "saga-delete",
            new SagaRepositoryTestState { CurrentState = "waiting" },
            0,
            SagaProgress.Empty,
            TestContext!.CancellationToken).ConfigureAwait(false);

        // Act
        await _repository.DeleteAsync("saga-delete", TestContext!.CancellationToken).ConfigureAwait(false);
        await _repository.DeleteAsync("saga-delete", TestContext!.CancellationToken).ConfigureAwait(false);
        var loaded = await _repository.LoadAsync("saga-delete", TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => loaded.Should().BeNull());
    }

    /// <summary>An unknown id returns null.</summary>
    [TestMethod]
    public async Task Load_UnknownIdReturnsNullAsync() {
        // Act
        var loaded = await _repository.LoadAsync("missing", TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => loaded.Should().BeNull());
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
}

/// <summary>A serializable state used by the repository contract.</summary>
public sealed class SagaRepositoryTestState : ISagaState {
    /// <summary>Gets or sets the transition-table position.</summary>
    public string CurrentState { get; set; } = "start";

    /// <summary>Gets or sets a payload value to prove round-tripping.</summary>
    public string? Value { get; set; }
}