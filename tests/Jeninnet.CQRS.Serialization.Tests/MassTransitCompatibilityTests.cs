//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Serialization.Tests;

/// <summary>
/// MassTransit's System.Text.Json serializer uses camelCase web defaults and a shaped envelope.
/// Our default JSON payload must survive that envelope unchanged, so a message produced by a
/// MassTransit peer can be read here (and vice versa) without a translation layer.
/// </summary>
[TestClass]
public sealed class MassTransitCompatibilityTests {
    private static readonly JsonSerializerOptions _webJson = new(JsonSerializerDefaults.Web);

    /// <summary>The System.Text.Json shape MassTransit uses for its JSON envelope.</summary>
    private sealed record MassTransitEnvelope(
        string MessageId,
        string? CorrelationId,
        string ConversationId,
        string? SourceAddress,
        string? DestinationAddress,
        IReadOnlyList<string> MessageType,
        JsonElement Message,
        DateTimeOffset SentTime,
        IReadOnlyDictionary<string, string> Headers);

    [TestMethod]
    public void OurJsonPayload_RoundTripsThroughAMassTransitShapedEnvelope() {
        // Arrange
        var serializer = new JsonMessageSerializer(Options.Create(new JsonMessageSerializerOptions()));
        var message = new SerializationContractMessage("mass-transit", 42, "interop");
        var payload = serializer.Serialize(message, typeof(SerializationContractMessage));
        using var messageDocument = JsonDocument.Parse(payload);
        var envelope = new MassTransitEnvelope(
            Guid.NewGuid().ToString(),
            null,
            Guid.NewGuid().ToString(),
            "rabbitmq://localhost/source",
            "rabbitmq://localhost/destination",
            ["urn:message:Jeninnet.CQRS.Serialization.Tests:SerializationContractMessage"],
            messageDocument.RootElement.Clone(),
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(StringComparer.Ordinal) {
                [TransportHeaders.ContentType] = JsonMessageSerializer.ContentTypeName,
            });
        var envelopeJson = JsonSerializer.Serialize(envelope, _webJson);
        using var parsedEnvelope = JsonDocument.Parse(envelopeJson);
        var extractedMessage = parsedEnvelope.RootElement.GetProperty("message").GetRawText();

        // Act
        var restored = serializer.Deserialize(Encoding.UTF8.GetBytes(extractedMessage), typeof(SerializationContractMessage));

        // Assert
        MSTestAssertions.That(() => restored.Should().Be(message));
        MSTestAssertions.That(() => envelopeJson.Should().Contain("\"messageType\""));
        MSTestAssertions.That(() => envelopeJson.Should().Contain("\"message\""));
    }

    [TestMethod]
    public void DefaultJsonPayload_UsesTheCamelCaseContractMassTransitExpects() {
        // Arrange
        var serializer = new JsonMessageSerializer(Options.Create(new JsonMessageSerializerOptions()));
        var message = new SerializationContractMessage("camel", 1, null);

        // Act
        var json = Encoding.UTF8.GetString(serializer.Serialize(message, typeof(SerializationContractMessage)));

        // Assert
        MSTestAssertions.That(() => json.Should().Contain("\"name\":\"camel\""));
        MSTestAssertions.That(() => json.Should().Contain("\"value\":1"));
        MSTestAssertions.That(() => json.Should().NotContain("\"Name\""));
    }

    [TestMethod]
    public void NewtonsoftPayload_UsesTheSameCamelCaseContract() {
        // Arrange
        var serializer = new NewtonsoftJsonMessageSerializer(Options.Create(new NewtonsoftJsonMessageSerializerOptions()));
        var message = new SerializationContractMessage("camel", 1, null);

        // Act
        var json = Encoding.UTF8.GetString(serializer.Serialize(message, typeof(SerializationContractMessage)));

        // Assert
        MSTestAssertions.That(() => json.Should().Contain("\"name\":\"camel\""));
        MSTestAssertions.That(() => json.Should().Contain("\"value\":1"));
    }
}