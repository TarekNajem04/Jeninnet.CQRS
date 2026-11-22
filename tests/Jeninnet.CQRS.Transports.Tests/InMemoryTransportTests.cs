//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports.Tests;

/// <summary>
/// Runs the shared transport contract suite against the in-memory transport. Every broker
/// package adds one derived class like this, so all transports are held to the same promises.
/// </summary>
[TestClass]
public sealed class InMemoryTransportTests : TransportContractTests {
    /// <inheritdoc />
    protected override Task<IMessageTransport> CreateTransportAsync(IMessageSerializer serializer, params IMessageTransportHandler[] handlers) =>
        Task.FromResult<IMessageTransport>(new InMemoryTransport(serializer, handlers, new TransportNamingConvention()));
}
