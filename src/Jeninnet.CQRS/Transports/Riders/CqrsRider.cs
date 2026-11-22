//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>Lifecycle state of one rider transport.</summary>
public enum CqrsRiderStatus {
    /// <summary>Registered but not started.</summary>
    Registered = 0,

    /// <summary>Start is in progress.</summary>
    Starting = 1,

    /// <summary>Started and accepting messages.</summary>
    Started = 2,

    /// <summary>Stop is in progress.</summary>
    Stopping = 3,

    /// <summary>Stopped.</summary>
    Stopped = 4,

    /// <summary>Start or stop failed.</summary>
    Faulted = 5,
}

/// <summary>
/// One named transport in a multi-transport host. Riders are created by
/// <c>AddCqrsRider</c>; the rider host owns their lifecycle and the routing table selects the
/// transport for each message type.
/// </summary>
public sealed class CqrsRider {
    /// <summary>Initializes a rider.</summary>
    /// <param name="name">The unique rider name used by routes and health/metrics tags.</param>
    /// <param name="transport">The transport. Never <see langword="null"/>.</param>
    public CqrsRider(string name, IMessageTransport transport) {
        Guard.NotNullOrWhiteSpace(name);

        Name = name;
        Transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    /// <summary>Gets the unique rider name.</summary>
    public string Name { get; }

    /// <summary>Gets the transport this rider owns.</summary>
    public IMessageTransport Transport { get; }

    /// <summary>Gets the current lifecycle state.</summary>
    public CqrsRiderStatus Status { get; internal set; } = CqrsRiderStatus.Registered;

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>Immutable point-in-time view of one rider for health and diagnostics.</summary>
/// <param name="Name">The unique rider name.</param>
/// <param name="Status">The lifecycle state.</param>
/// <param name="TransportType">The concrete transport type name.</param>
public sealed record CqrsRiderSnapshot(string Name, CqrsRiderStatus Status, string TransportType);

/// <summary>One message-type route to a named rider.</summary>
/// <param name="MessageType">The message type.</param>
/// <param name="RiderName">The destination rider name.</param>
public sealed record CqrsRiderRoute(Type MessageType, string RiderName);

/// <summary>Reports a message type that has no rider route at send time.</summary>
public sealed class CqrsRiderRoutingException : InvalidOperationException {
    /// <summary>Initializes the exception.</summary>
    /// <param name="messageType">The unrouteable message type.</param>
    /// <param name="message">The human-readable failure.</param>
    public CqrsRiderRoutingException(Type messageType, string message) : base(message) {
        MessageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
    }

    /// <summary>Gets the unrouteable message type.</summary>
    public Type MessageType { get; }
}

/// <summary>
/// Reports invalid rider configuration discovered at host startup. The findings list every
/// problem instead of failing on the first one.
/// </summary>
public sealed class CqrsRiderConfigurationException : InvalidOperationException {
    /// <summary>Initializes the exception.</summary>
    /// <param name="findings">The validation findings.</param>
    public CqrsRiderConfigurationException(IReadOnlyList<string> findings)
        : base(string.Join(Environment.NewLine, findings ?? throw new ArgumentNullException(nameof(findings)))) {
        Findings = findings;
    }

    /// <summary>Gets the validation findings.</summary>
    public IReadOnlyList<string> Findings { get; }
}
