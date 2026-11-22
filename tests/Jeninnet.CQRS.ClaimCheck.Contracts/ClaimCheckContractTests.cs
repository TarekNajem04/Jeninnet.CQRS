//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.ClaimCheck.Contracts;

/// <summary>
/// Reusable contract every <see cref="IClaimCheckStore"/> provider must satisfy. The
/// file-system, Azure Blob, and Amazon S3 suites derive from it; each supplies a fresh store and
/// its maintenance surface.
/// </summary>
[TestClass]
public abstract class ClaimCheckContractTests {
    /// <summary>Gets or sets the MSTest context (used for cancellation).</summary>
    public TestContext? TestContext { get; set; }

    /// <summary>Gets the provider under test.</summary>
    protected abstract IClaimCheckStore Store { get; }

    /// <summary>Gets the maintenance surface of the same provider instance.</summary>
    protected abstract IClaimCheckStoreMaintenance Maintenance { get; }

    private CancellationToken Token => TestContext!.CancellationToken;

    /// <summary>A payload round-trips with its content type, and delete is idempotent.</summary>
    [TestMethod]
    public async Task PutGetDelete_RoundTripsThePayloadAndContentTypeAsync() {
        // Arrange
        await ResetAsync().ConfigureAwait(false);
        var payload = NewPayload("round-trip");

        // Act
        var key = await Store.PutAsync(payload, "application/json", Token).ConfigureAwait(false);
        var loaded = await Store.GetAsync(key, Token).ConfigureAwait(false);
        await Store.DeleteAsync(key, Token).ConfigureAwait(false);
        var deleted = await Store.GetAsync(key, Token).ConfigureAwait(false);
        await Store.DeleteAsync(key, Token).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => key.Should().NotBeNullOrWhiteSpace());
        MSTestAssertions.That(() => loaded.Should().NotBeNull());
        var claimed = loaded!;
        MSTestAssertions.That(() => claimed.ContentType.Should().Be("application/json"));
        MSTestAssertions.That(() => claimed.Payload.Should().Equal(payload));
        MSTestAssertions.That(() => deleted.Should().BeNull());
    }

    /// <summary>Equal bytes and metadata deduplicate to one content-addressed entry.</summary>
    [TestMethod]
    public async Task Put_SamePayloadTwice_KeepsOneContentAddressedEntryAsync() {
        // Arrange
        await ResetAsync().ConfigureAwait(false);
        var payload = NewPayload("dedupe");

        // Act
        var first = await Store.PutAsync(payload, "application/json", Token).ConfigureAwait(false);
        var second = await Store.PutAsync(payload, "application/json", Token).ConfigureAwait(false);
        var count = await Maintenance.CountAsync(Token).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => first.Should().Be(second));
        MSTestAssertions.That(() => count.Should().Be(1));
    }

    /// <summary>Cleanup respects the age cutoff and never deletes referenced payloads.</summary>
    [TestMethod]
    public async Task Cleanup_RemovesOnlyExpiredUnreferencedPayloadsAsync() {
        // Arrange
        await ResetAsync().ConfigureAwait(false);
        var referenced = await Store.PutAsync(NewPayload("referenced"), "application/json", Token).ConfigureAwait(false);
        var expired = await Store.PutAsync(NewPayload("expired"), "application/json", Token).ConfigureAwait(false);
        await Store.PutAsync(NewPayload("fresh"), "application/json", Token).ConfigureAwait(false);

        // Act: a cutoff in the past expires nothing.
        var premature = await Maintenance.CleanupAsync(
            DateTimeOffset.UtcNow.AddDays(-1),
            Array.Empty<string>(),
            Token).ConfigureAwait(false);

        // Act: a future cutoff expires everything except the referenced key.
        var deleted = await Maintenance.CleanupAsync(
            DateTimeOffset.UtcNow.AddDays(1),
            new[] { referenced },
            Token).ConfigureAwait(false);
        var referencedPayload = await Store.GetAsync(referenced, Token).ConfigureAwait(false);
        var expiredPayload = await Store.GetAsync(expired, Token).ConfigureAwait(false);
        var remaining = await Maintenance.CountAsync(Token).ConfigureAwait(false);

        // Act: without references, the last payload is collectable too.
        var collected = await Maintenance.CleanupAsync(
            DateTimeOffset.UtcNow.AddDays(1),
            Array.Empty<string>(),
            Token).ConfigureAwait(false);
        var after = await Maintenance.CountAsync(Token).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => premature.Should().Be(0));
        MSTestAssertions.That(() => deleted.Should().Be(2));
        MSTestAssertions.That(() => referencedPayload.Should().NotBeNull());
        MSTestAssertions.That(() => expiredPayload.Should().BeNull());
        MSTestAssertions.That(() => remaining.Should().Be(1));
        MSTestAssertions.That(() => collected.Should().Be(1));
        MSTestAssertions.That(() => after.Should().Be(0));
    }

    private Task ResetAsync() =>
        Maintenance.CleanupAsync(DateTimeOffset.MaxValue, Array.Empty<string>(), Token);

    private static byte[] NewPayload(string marker) =>
        Encoding.UTF8.GetBytes($"{marker}:{Guid.NewGuid():N}");
}
