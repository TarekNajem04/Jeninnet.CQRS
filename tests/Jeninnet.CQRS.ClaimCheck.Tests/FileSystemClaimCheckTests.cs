//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.ClaimCheck.Tests;

/// <summary>Runs the shared claim-check contract against the file-system provider.</summary>
[TestClass]
public sealed class FileSystemClaimCheckTests : ClaimCheckContractTests {
    private string _root = null!;
    private FileSystemClaimCheckStore _store = null!;

    /// <inheritdoc />
    protected override IClaimCheckStore Store => _store;

    /// <inheritdoc />
    protected override IClaimCheckStoreMaintenance Maintenance => _store;

    /// <summary>Creates an isolated directory per test.</summary>
    [TestInitialize]
    public void Initialize() {
        _root = Path.Combine(Path.GetTempPath(), $"cqrs62-claim-check-{Guid.NewGuid():N}");
        _store = new FileSystemClaimCheckStore(_root);
    }

    /// <summary>A new store instance reads a payload written by the previous one.</summary>
    [TestMethod]
    public async Task ReopenedStore_ReadsThePersistedPayloadAsync() {
        // Arrange
        var payload = Encoding.UTF8.GetBytes("restart:" + Guid.NewGuid().ToString("N"));
        var key = await _store.PutAsync(payload, "application/json", TestContext!.CancellationToken).ConfigureAwait(false);

        // Act
        var reopened = new FileSystemClaimCheckStore(_root);
        var loaded = await reopened.GetAsync(key, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => loaded.Should().NotBeNull());
        var claimed = loaded!;
        MSTestAssertions.That(() => claimed.ContentType.Should().Be("application/json"));
        MSTestAssertions.That(() => claimed.Payload.Should().Equal(payload));
    }

    /// <summary>Removes the per-test directory.</summary>
    [TestCleanup]
    public void Cleanup() {
        if(Directory.Exists(_root)) {
            Directory.Delete(_root, recursive: true);
        }
    }
}
