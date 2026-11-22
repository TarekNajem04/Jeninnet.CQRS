//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.AzureServiceBus;

/// <summary>
/// Authentication, topology, session, and receiver options for the Azure Service Bus transport.
/// </summary>
public sealed class AzureServiceBusTransportOptions {
    /// <summary>
    /// Gets or sets the connection string (including the emulator's development connection).
    /// Mutually exclusive with <see cref="FullyQualifiedNamespace"/> + <see cref="Credential"/>.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>Gets or sets the fully qualified namespace for managed-identity authentication.</summary>
    public string? FullyQualifiedNamespace { get; set; }

    /// <summary>Gets or sets the token credential used with <see cref="FullyQualifiedNamespace"/>.</summary>
    public Azure.Core.TokenCredential? Credential { get; set; }

    /// <summary>
    /// Gets or sets whether queues, topics, and subscriptions are created on start. Defaults to
    /// <see langword="true"/>; set it to <see langword="false"/> when infrastructure is provisioned
    /// separately.
    /// </summary>
    public bool EnsureTopology { get; set; } = true;

    /// <summary>
    /// Gets or sets the subscription name prefix. One handler uses the prefix, additional handlers
    /// use <c>{prefix}.{index}</c>. Defaults to <c>cqrs</c>.
    /// </summary>
    public string SubscriptionPrefix { get; set; } = "cqrs";

    /// <summary>
    /// Gets or sets whether queues/subscriptions require sessions. Defaults to
    /// <see langword="false"/>; when enabled, <see cref="Transports.TransportMessageOptions.PartitionKey"/>
    /// becomes the session id and ordering is preserved per session.
    /// </summary>
    public bool SessionsEnabled { get; set; }

    /// <summary>Gets or sets the receiver prefetch count. Defaults to 0.</summary>
    public int PrefetchCount { get; set; }

    /// <summary>Gets or sets the receive wait time per poll. Defaults to one second.</summary>
    public TimeSpan ReceiveWaitTime { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets the entity lock duration. Defaults to five minutes.</summary>
    public TimeSpan LockDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets the message types this transport consumes.</summary>
    public IList<AzureServiceBusSubscription> Subscriptions { get; } = [];
}