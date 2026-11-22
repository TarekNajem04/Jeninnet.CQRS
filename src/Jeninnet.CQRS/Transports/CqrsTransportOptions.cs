//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transports;

/// <summary>The messaging path selected by the application.</summary>
public enum CqrsTransportMode {
    /// <summary>Execute through the in-process relay only (default).</summary>
    InProcess = 0,

    /// <summary>Route messages through the in-memory transport.</summary>
    InMemory = 1,

    /// <summary>A broker package registered an external transport (for example RabbitMQ or Kafka).</summary>
    External = 2,
}

/// <summary>
/// Transport selection options. The default stays <see cref="CqrsTransportMode.InProcess"/>, so
/// registering a transport is an explicit choice; outbox relay workers and other
/// transport-aware components read this flag.
/// </summary>
public sealed class CqrsTransportOptions {
    /// <summary>Gets or sets the selected messaging path. Defaults to in-process.</summary>
    public CqrsTransportMode Mode { get; set; } = CqrsTransportMode.InProcess;
}