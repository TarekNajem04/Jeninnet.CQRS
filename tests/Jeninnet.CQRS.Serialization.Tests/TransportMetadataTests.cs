//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Serialization.Tests;

/// <summary>
/// Versioned envelope metadata is broker-neutral: every transport writes the same headers, and
/// consumers read them back with a legacy fallback for messages produced before standardization.
/// </summary>
[TestClass]
public sealed class TransportMetadataTests {
    [TestMethod]
    public void SetMetadata_WritesTypeVersionAndContentType() {
        // Arrange
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);

        // Act
        TransportHeaders.SetMetadata(headers, "My.Message, My.Assembly", 3, MessagePackMessageSerializer.ContentTypeName);

        // Assert
        MSTestAssertions.That(() => headers[TransportHeaders.MessageType].Should().Be("My.Message, My.Assembly"));
        MSTestAssertions.That(() => headers[TransportHeaders.MessageVersion].Should().Be("3"));
        MSTestAssertions.That(() => headers[TransportHeaders.ContentType].Should().Be(MessagePackMessageSerializer.ContentTypeName));
        MSTestAssertions.That(() => TransportHeaders.GetMessageType(headers).Should().Be("My.Message, My.Assembly"));
        MSTestAssertions.That(() => TransportHeaders.GetMessageVersion(headers).Should().Be(3));
        MSTestAssertions.That(() => TransportHeaders.GetContentType(headers).Should().Be(MessagePackMessageSerializer.ContentTypeName));
    }

    [TestMethod]
    public void GetMessageType_FallsBackToTheLegacyHeader() {
        // Arrange
        var headers = new Dictionary<string, string>(StringComparer.Ordinal) {
            [TransportHeaders.LegacyMessageType] = "Legacy.Message",
        };

        // Act
        var messageType = TransportHeaders.GetMessageType(headers);

        // Assert
        MSTestAssertions.That(() => messageType.Should().Be("Legacy.Message"));
    }

    [TestMethod]
    public void GetMessageVersion_MissingOrMalformed_FallsBackToOne() {
        // Arrange
        var missing = new Dictionary<string, string>(StringComparer.Ordinal);
        var malformed = new Dictionary<string, string>(StringComparer.Ordinal) {
            [TransportHeaders.MessageVersion] = "not-a-number",
        };

        // Act
        var missingVersion = TransportHeaders.GetMessageVersion(missing);
        var malformedVersion = TransportHeaders.GetMessageVersion(malformed);

        // Assert
        MSTestAssertions.That(() => missingVersion.Should().Be(1));
        MSTestAssertions.That(() => malformedVersion.Should().Be(1));
    }

    [TestMethod]
    public void SetMetadata_RejectsVersionsBelowOne() {
        // Arrange
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);

        // Act
        var thrown = Capture(() => TransportHeaders.SetMetadata(headers, "My.Message", 0, JsonMessageSerializer.ContentTypeName));

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<ArgumentOutOfRangeException>());
    }

    [TestMethod]
    public async Task InMemoryTransport_CarriesTheMessageVersionAndMetadataHeadersAsync() {
        // Arrange
        var serializer = new JsonMessageSerializer(Options.Create(new JsonMessageSerializerOptions()));
        var probe = new TransportProbe();
        IMessageTransport transport = new InMemoryTransport(serializer, [probe], new TransportNamingConvention());
        await transport.StartAsync().ConfigureAwait(false);

        try {
            // Act
            await transport.SendAsync(
                new SerializationContractMessage("versioned", 5, null),
                new TransportMessageOptions { MessageVersion = 4 }).ConfigureAwait(false);
        }
        finally {
            await transport.StopAsync().ConfigureAwait(false);
        }

        // Assert
        var envelope = probe.Received.Single();
        MSTestAssertions.That(() => envelope.MessageVersion.Should().Be(4));
        MSTestAssertions.That(() => envelope.Headers[TransportHeaders.MessageVersion].Should().Be("4"));
        MSTestAssertions.That(() => envelope.Headers[TransportHeaders.ContentType].Should().Be(JsonMessageSerializer.ContentTypeName));
    }

    [TestMethod]
    public async Task InMemoryTransport_RejectsAnInvalidMessageVersionAsync() {
        // Arrange
        var serializer = new JsonMessageSerializer(Options.Create(new JsonMessageSerializerOptions()));
        IMessageTransport transport = new InMemoryTransport(serializer, [], new TransportNamingConvention());
        await transport.StartAsync().ConfigureAwait(false);

        try {
            // Act
            var thrown = await CaptureAsync(() => transport.SendAsync(
                new SerializationContractMessage("invalid", 0, null),
                new TransportMessageOptions { MessageVersion = 0 })).ConfigureAwait(false);

            // Assert
            MSTestAssertions.That(() => thrown.Should().BeOfType<ArgumentOutOfRangeException>());
        }
        finally {
            await transport.StopAsync().ConfigureAwait(false);
        }
    }

    private static Exception? Capture(Action action) {
        try {
            action();

            return null;
        }
        catch(Exception exception) {
            return exception;
        }
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
