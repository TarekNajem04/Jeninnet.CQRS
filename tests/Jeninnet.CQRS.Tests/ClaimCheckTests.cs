//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// Characterization tests for CQRS-28 core seams: the default JSON serializer preserves the
/// historical payload shape, and the claim-check policy stages only payloads above the
/// threshold while failing loudly on missing references or content-type mismatches.
/// </summary>
[TestClass]
public class ClaimCheckTests {
    [TestMethod]
    public void JsonSerializer_RoundTripsACommand() {
        // Arrange
        var serializer = new JsonMessageSerializer(Options.Create(new JsonMessageSerializerOptions()));
        var command = new ClaimCheckCommand("hello");

        // Act
        var bytes = serializer.Serialize(command, typeof(ClaimCheckCommand));
        var restored = serializer.Deserialize(bytes, typeof(ClaimCheckCommand));

        // Assert
        MSTestAssertions.That(() => serializer.ContentType.Should().Be("application/json"));
        MSTestAssertions.That(() => restored.Should().Be(command));
    }

    [TestMethod]
    public void Key_IsContentAddressedAndContentTypeSensitive() {
        // Arrange
        var payload = new byte[] { 1, 2, 3, 4 };

        // Act
        var first = ClaimCheckKey.Compute(payload, "application/json");
        var duplicate = ClaimCheckKey.Compute(payload, "application/json");
        var otherType = ClaimCheckKey.Compute(payload, "application/x-custom");
        var otherPayload = ClaimCheckKey.Compute(new byte[] { 1, 2, 3, 5 }, "application/json");

        // Assert
        MSTestAssertions.That(() => first.Should().Be(duplicate));
        MSTestAssertions.That(() => first.Length.Should().Be(64));
        MSTestAssertions.That(() => first.Should().Be(first.ToLowerInvariant()));
        MSTestAssertions.That(() => first.Should().NotBe(otherType));
        MSTestAssertions.That(() => first.Should().NotBe(otherPayload));
    }

    [TestMethod]
    public async Task Policy_StagesOnlyPayloadsAboveThresholdAsync() {
        // Arrange
        var store = new InMemoryClaimCheckStore();
        var policy = new ClaimCheckPolicy(Options.Create(new ClaimCheckOptions { ThresholdBytes = 4 }), [store]);

        // Act
        var small = await policy.StageIfLargeAsync(new byte[4], "application/json", default);
        var large = await policy.StageIfLargeAsync(new byte[5], "application/json", default);
        var claim = await policy.GetClaimAsync(large, "application/json", default);

        // Assert
        MSTestAssertions.That(() => small.Should().BeNull());
        MSTestAssertions.That(() => large.Should().NotBeNull());
        MSTestAssertions.That(() => claim!.Length.Should().Be(5));

        await policy.DeleteAsync(large, default);
        MSTestAssertions.That(() => store.GetAsync(large!, default).GetAwaiter().GetResult().Should().BeNull());
    }

    [TestMethod]
    public async Task Policy_FailsLoudly_OnContentTypeMismatchAsync() {
        // Arrange
        var store = new InMemoryClaimCheckStore();
        var policy = new ClaimCheckPolicy(Options.Create(new ClaimCheckOptions { ThresholdBytes = 0 }), [store]);
        var key = await policy.StageIfLargeAsync(new byte[1], "application/x-custom", default);

        // Act
        var thrown = await CaptureAsync(() => policy.GetClaimAsync(key, "application/json", default));

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<InvalidOperationException>());
        MSTestAssertions.That(() => thrown!.Message.Should().Contain("application/x-custom"));
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

    private sealed record ClaimCheckCommand(string Name) : ICommand;
}