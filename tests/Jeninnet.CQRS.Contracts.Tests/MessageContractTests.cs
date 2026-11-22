//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Contracts.Tests;

/// <summary>
/// CQRS-65 standalone contracts: message marker types compile with no core reference, the
/// version attribute and headers are stable, and the assembly has no Jeninnet dependencies.
/// </summary>
[TestClass]
public sealed class MessageContractTests {
    [TestMethod]
    public void MessageMarkers_ComposeWithoutTheCoreRuntime() {
        // Arrange / Act
        var command = new ContractCommand("O-1");
        var query = new ContractQuery("O-1");
        var notification = new ContractNotification("O-1");
        var stream = new ContractStream("O-1");

        // Assert
        MSTestAssertions.That(() => command.Should().BeAssignableTo<ICommand>());
        MSTestAssertions.That(() => query.Should().BeAssignableTo<IQuery<string>>());
        MSTestAssertions.That(() => notification.Should().BeAssignableTo<INotification>());
        MSTestAssertions.That(() => notification.Should().BeAssignableTo<IEvent>());
        MSTestAssertions.That(() => stream.Should().BeAssignableTo<IStreamQuery<int>>());
        MSTestAssertions.That(() => typeof(ICommand).GetInterfaces().Should().Contain(typeof(ICommand<Unit>)));
    }

    [TestMethod]
    public void MessageVersion_IsMetadataAndRejectsNonPositiveVersions() {
        // Arrange
        var attribute = typeof(VersionedEvent).GetCustomAttribute<MessageVersionAttribute>();

        // Act
        Exception? thrown = null;

        try {
            _ = new MessageVersionAttribute(0);
        }
        catch(Exception exception) {
            thrown = exception;
        }

        // Assert
        MSTestAssertions.That(() => attribute.Should().NotBeNull());
        MSTestAssertions.That(() => attribute!.Version.Should().Be(2));
        MSTestAssertions.That(() => thrown.Should().BeOfType<ArgumentOutOfRangeException>());
    }

    [TestMethod]
    public void Headers_HaveStableWireValues() {
        // Assert
        MSTestAssertions.That(() => MessageHeaders.MessageType.Should().Be("message-type"));
        MSTestAssertions.That(() => MessageHeaders.MessageVersion.Should().Be("message-version"));
        MSTestAssertions.That(() => MessageHeaders.ContentType.Should().Be("content-type"));
        MSTestAssertions.That(() => MessageHeaders.Tenant.Should().Be("x-tenant-id"));
        MSTestAssertions.That(() => MessageHeaders.LegacyMessageType.Should().Be("x-message-type"));
    }

    [TestMethod]
    public void ContractsAssembly_HasNoJeninnetReferences() {
        // Act
        var references = typeof(ICommand).Assembly
            .GetReferencedAssemblies()
            .Select(static assembly => assembly.Name ?? string.Empty)
            .ToArray();

        // Assert
        MSTestAssertions.That(() => references.Should().NotContain(name => name.StartsWith("Jeninnet.", StringComparison.Ordinal)));
    }

    private sealed record ContractCommand(string Id) : ICommand;

    private sealed record ContractQuery(string Id) : IQuery<string>;

    private sealed record ContractNotification(string Id) : INotification;

    private sealed record ContractStream(string Id) : IStreamQuery<int>;

    [MessageVersion(2)]
    private sealed record VersionedEvent(string Id) : IEvent;
}
