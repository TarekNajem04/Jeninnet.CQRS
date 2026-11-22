//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// CQRS-65 migration: the core assembly forwards the moved message types, so existing binaries
/// and source naming `Jeninnet.CQRS.Interfaces.*` resolve to the contracts assembly.
/// </summary>
[TestClass]
public sealed class MessageContractForwardingTests {
    [TestMethod]
    public void CoreAssembly_ForwardsMessageTypesToContracts() {
        // Act
        var forwarded = Type.GetType("Jeninnet.CQRS.Interfaces.ICommand, Jeninnet.CQRS", throwOnError: false);
        var forwardedUnit = Type.GetType("Jeninnet.CQRS.Unit, Jeninnet.CQRS", throwOnError: false);

        // Assert
        MSTestAssertions.That(() => forwarded.Should().Be(typeof(ICommand)));
        MSTestAssertions.That(() => forwarded!.Assembly.GetName().Name.Should().Be("Jeninnet.CQRS.Contracts"));
        MSTestAssertions.That(() => forwardedUnit.Should().Be(typeof(Unit)));
        MSTestAssertions.That(() => typeof(MessageVersionAttribute).Assembly.GetName().Name.Should().Be("Jeninnet.CQRS.Contracts"));
    }

    [TestMethod]
    public void CoreTransportHeaders_MatchTheContractsValues() {
        // Assert
        MSTestAssertions.That(() => TransportHeaders.MessageType.Should().Be(MessageHeaders.MessageType));
        MSTestAssertions.That(() => TransportHeaders.MessageVersion.Should().Be(MessageHeaders.MessageVersion));
        MSTestAssertions.That(() => TransportHeaders.ContentType.Should().Be(MessageHeaders.ContentType));
        MSTestAssertions.That(() => TransportHeaders.Tenant.Should().Be(MessageHeaders.Tenant));
        MSTestAssertions.That(() => TransportHeaders.LegacyMessageType.Should().Be(MessageHeaders.LegacyMessageType));
    }
}
