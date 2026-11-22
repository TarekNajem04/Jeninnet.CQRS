//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Serialization.Tests;

/// <summary>
/// Content-type negotiation is the heart of the feature: the same message must round-trip through
/// all three formats, and an unknown content type must fail with an actionable error rather than
/// being misparsed as JSON.
/// </summary>
[TestClass]
public sealed class MessageSerializerRegistryTests {
    private static readonly SerializationContractMessage _message = new("negotiated", 7, "note");

    [TestMethod]
    public void Resolve_KnownContentType_IgnoresCaseAndParameters() {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var resolved = registry.Resolve("APPLICATION/X-MSGPACK; charset=binary");

        // Assert
        MSTestAssertions.That(() => resolved.Should().BeOfType<MessagePackMessageSerializer>());
    }

    [TestMethod]
    public void Resolve_MissingContentType_ReturnsTheDefaultSerializer() {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var resolved = registry.Resolve(null);

        // Assert
        MSTestAssertions.That(() => resolved.Should().BeSameAs(registry.Default));
        MSTestAssertions.That(() => resolved.Should().BeOfType<JsonMessageSerializer>());
    }

    [TestMethod]
    public void Resolve_UnknownContentType_ThrowsAnActionableDeadLetterError() {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var thrown = Capture(() => registry.Resolve("application/x-protobuf"));

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<UnknownContentTypeException>());
        var error = (UnknownContentTypeException)thrown!;
        MSTestAssertions.That(() => error.ContentType.Should().Be("application/x-protobuf"));
        MSTestAssertions.That(() => error.Message.Should().Contain("application/x-protobuf"));
        MSTestAssertions.That(() => error.Message.Should().Contain(JsonMessageSerializer.ContentTypeName));
        MSTestAssertions.That(() => error.Message.Should().Contain(MessagePackMessageSerializer.ContentTypeName));
        MSTestAssertions.That(() => error.Message.Should().Contain(NewtonsoftJsonMessageSerializer.ContentTypeName));
        MSTestAssertions.That(() => error.SupportedContentTypes.Should().HaveCount(3));
    }

    [TestMethod]
    public void TryResolve_UnknownContentType_ReturnsFalseWithoutThrowing() {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var resolved = registry.TryResolve("application/xml", out var serializer);

        // Assert
        MSTestAssertions.That(() => resolved.Should().BeFalse());
        MSTestAssertions.That(() => serializer.Should().BeNull());
    }

    [TestMethod]
    public void Deserialize_UsesTheSerializerNamedByTheContentType() {
        // Arrange
        var registry = CreateRegistry();
        var formats = new (IMessageSerializer Serializer, string ContentType)[] {
            (new JsonMessageSerializer(Options.Create(new JsonMessageSerializerOptions())), JsonMessageSerializer.ContentTypeName),
            (new NewtonsoftJsonMessageSerializer(Options.Create(new NewtonsoftJsonMessageSerializerOptions())), NewtonsoftJsonMessageSerializer.ContentTypeName),
            (new MessagePackMessageSerializer(Options.Create(new MessagePackMessageSerializerOptions())), MessagePackMessageSerializer.ContentTypeName),
        };

        foreach(var (serializer, contentType) in formats) {
            // Act
            var payload = serializer.Serialize(_message, typeof(SerializationContractMessage));
            var restored = registry.Deserialize(payload, typeof(SerializationContractMessage), contentType);

            // Assert
            MSTestAssertions.That(() => restored.Should().Be(_message, $"the {contentType} format must round-trip"));
        }
    }

    [TestMethod]
    public void TransportMessageReader_NegotiatesByEnvelopeContentType() {
        // Arrange
        var registry = CreateRegistry();
        var reader = new TransportMessageReader(registry);
        var serializer = new MessagePackMessageSerializer(Options.Create(new MessagePackMessageSerializerOptions()));
        var envelope = CreateEnvelope(
            serializer.Serialize(_message, typeof(SerializationContractMessage)),
            MessagePackMessageSerializer.ContentTypeName);

        // Act
        var restored = reader.Deserialize(envelope, typeof(SerializationContractMessage));

        // Assert
        MSTestAssertions.That(() => restored.Should().Be(_message));
    }

    [TestMethod]
    public void TransportMessageReader_UnknownContentType_IsReportedForDeadLettering() {
        // Arrange
        var reader = new TransportMessageReader(CreateRegistry());
        var envelope = CreateEnvelope([1, 2, 3], "application/x-protobuf");

        // Act
        var read = reader.TryDeserialize(envelope, typeof(SerializationContractMessage), out var message, out var error);

        // Assert
        MSTestAssertions.That(() => read.Should().BeFalse());
        MSTestAssertions.That(() => message.Should().BeNull());
        MSTestAssertions.That(() => error.Should().NotBeNull());
        MSTestAssertions.That(() => error!.ContentType.Should().Be("application/x-protobuf"));
        MSTestAssertions.That(() => error!.Message.Should().Contain("dead-letter"));
    }

    [TestMethod]
    public void TransportMessageReader_MissingContentType_UsesTheDefaultFormat() {
        // Arrange
        var reader = new TransportMessageReader(CreateRegistry());
        var serializer = new JsonMessageSerializer(Options.Create(new JsonMessageSerializerOptions()));
        var envelope = CreateEnvelope(
            serializer.Serialize(_message, typeof(SerializationContractMessage)),
            string.Empty);

        // Act
        var restored = reader.Deserialize(envelope, typeof(SerializationContractMessage));

        // Assert
        MSTestAssertions.That(() => restored.Should().Be(_message));
    }

    [TestMethod]
    public async Task UnknownContentType_IsRecordedInTheDeadLetterStoreWithTheOriginalEnvelopeAsync() {
        // Arrange
        var store = new InMemoryDeadLetterStore(Options.Create(new DeadLetterOptions()));
        var reader = new TransportMessageReader(CreateRegistry());
        var envelope = CreateEnvelope([1, 2, 3], "application/x-protobuf");

        // Act
        var read = reader.TryDeserialize(envelope, typeof(SerializationContractMessage), out _, out var error);
        var key = await store.RecordAsync(new TransportEnvelopeDeadLetter(envelope), error!, DateTimeOffset.UtcNow).ConfigureAwait(false);
        var entry = await store.GetAsync(key).ConfigureAwait(false);

        // Assert: the poison message is inspectable data, never a silent JSON fallback.
        MSTestAssertions.That(() => read.Should().BeFalse());
        MSTestAssertions.That(() => entry.Should().NotBeNull());
        MSTestAssertions.That(() => entry!.LastErrorType.Should().Be(typeof(UnknownContentTypeException).FullName));
        MSTestAssertions.That(() => entry!.LastErrorMessage.Should().Contain("application/x-protobuf"));
        MSTestAssertions.That(() => entry!.CommandType.Should().Be(envelope.MessageType));
        MSTestAssertions.That(() => entry!.Envelope.Command.Should().BeSameAs(envelope));
    }

    [TestMethod]
    public void AddCqrsFormatPackages_KeepJsonAsDefaultAndRegisterEveryFormat() {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCqrsNewtonsoftJsonSerialization();
        services.AddCqrsMessagePackSerialization();
        using var provider = services.BuildServiceProvider();
        var defaultSerializer = provider.GetRequiredService<IMessageSerializer>();
        var registry = provider.GetRequiredService<IMessageSerializerRegistry>();
        var reader = provider.GetRequiredService<TransportMessageReader>();

        // Assert
        MSTestAssertions.That(() => defaultSerializer.Should().BeOfType<JsonMessageSerializer>());
        MSTestAssertions.That(() => registry.ContentTypes.Should().HaveCount(3));
        MSTestAssertions.That(() => registry.ContentTypes.Should().Contain(JsonMessageSerializer.ContentTypeName));
        MSTestAssertions.That(() => registry.ContentTypes.Should().Contain(NewtonsoftJsonMessageSerializer.ContentTypeName));
        MSTestAssertions.That(() => registry.ContentTypes.Should().Contain(MessagePackMessageSerializer.ContentTypeName));
        MSTestAssertions.That(() => reader.Serializers.Should().BeSameAs(registry));
    }

    [TestMethod]
    public void AddCqrsMessagePackSerialization_WithoutExplicitCoreRegistration_StillUsesJsonDefault() {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCqrsMessagePackSerialization();
        using var provider = services.BuildServiceProvider();

        // Assert
        MSTestAssertions.That(() => provider.GetRequiredService<IMessageSerializer>().Should().BeOfType<JsonMessageSerializer>());
        MSTestAssertions.That(() => provider.GetRequiredService<IMessageSerializerRegistry>().ContentTypes.Should().HaveCount(2));
    }

    [TestMethod]
    public void CoreAssembly_DoesNotReferenceOptionalFormatLibraries() {
        // Act
        var references = typeof(IMessageSerializer).Assembly
            .GetReferencedAssemblies()
            .Select(static assembly => assembly.Name ?? string.Empty)
            .ToList();

        // Assert
        MSTestAssertions.That(() => references.Should().NotContain("Newtonsoft.Json"));
        MSTestAssertions.That(() => references.Should().NotContain("MessagePack"));
    }

    private static MessageSerializerRegistry CreateRegistry() =>
        new(
            new JsonMessageSerializer(Options.Create(new JsonMessageSerializerOptions())),
            [
                new MessageSerializerFormat(new NewtonsoftJsonMessageSerializer(Options.Create(new NewtonsoftJsonMessageSerializerOptions()))),
                new MessageSerializerFormat(new MessagePackMessageSerializer(Options.Create(new MessagePackMessageSerializerOptions()))),
            ]);

    private static TransportEnvelope CreateEnvelope(byte[] payload, string contentType) =>
        new(
            Guid.NewGuid().ToString("N"),
            typeof(SerializationContractMessage).FullName ?? nameof(SerializationContractMessage),
            contentType,
            payload,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(StringComparer.Ordinal),
            new TransportTopology("exchange", "queue", "topic", "routing-key"),
            TransportDeliveryMode.Send);

    private static Exception? Capture(Action action) {
        try {
            action();

            return null;
        }
        catch(Exception exception) {
            return exception;
        }
    }
}
