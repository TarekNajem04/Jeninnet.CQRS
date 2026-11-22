//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Serialization.Tests;

/// <summary>
/// The only acceptance surface for a wire format: the same message round-trips, the declared
/// content type is stable, and the serializer is safe to call repeatedly (singletons are shared).
/// </summary>
public abstract class SerializerContractTests {
    /// <summary>Creates a fresh serializer for the format under test.</summary>
    protected abstract IMessageSerializer CreateSerializer();

    /// <summary>Gets the content type the format must declare.</summary>
    protected abstract string ExpectedContentType { get; }

    /// <summary>A record with strings, numbers, and a null round-trips unchanged.</summary>
    [TestMethod]
    public void Serialize_ThenDeserialize_RoundTripsTheMessage() {
        // Arrange
        var serializer = CreateSerializer();
        var message = new SerializationContractMessage("hello", 42, null);

        // Act
        var payload = serializer.Serialize(message, typeof(SerializationContractMessage));
        var restored = serializer.Deserialize(payload, typeof(SerializationContractMessage));

        // Assert
        MSTestAssertions.That(() => restored.Should().Be(message));
        MSTestAssertions.That(() => payload.Length.Should().BeGreaterThan(0));
    }

    /// <summary>The declared content type is exactly the negotiated wire name.</summary>
    [TestMethod]
    public void ContentType_IsTheDeclaredWireName() {
        // Act
        var serializer = CreateSerializer();

        // Assert
        MSTestAssertions.That(() => serializer.ContentType.Should().Be(ExpectedContentType));
    }

    /// <summary>Serialization does not consume or mutate the message between calls.</summary>
    [TestMethod]
    public void Serializer_IsReusableAcrossCalls() {
        // Arrange
        var serializer = CreateSerializer();
        var first = new SerializationContractMessage("first", 1, "note");
        var second = new SerializationContractMessage("second", 2, null);

        // Act
        var firstBytes = serializer.Serialize(first, typeof(SerializationContractMessage));
        var secondBytes = serializer.Serialize(second, typeof(SerializationContractMessage));
        var firstRestored = serializer.Deserialize(firstBytes, typeof(SerializationContractMessage));
        var secondRestored = serializer.Deserialize(secondBytes, typeof(SerializationContractMessage));

        // Assert
        MSTestAssertions.That(() => firstRestored.Should().Be(first));
        MSTestAssertions.That(() => secondRestored.Should().Be(second));
    }
}
