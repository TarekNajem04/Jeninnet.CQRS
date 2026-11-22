# ADR-0011 — MQTT stays on MQTTnet 4.3 for netstandard2.0 reach

**Status:** Accepted · **Date:** 2026-11-19

## Context

`CQRS-67` adds `Jeninnet.CQRS.Transport.Mqtt`. MQTTnet 5.2 targets `net8.0`/`net10.0` only;
MQTTnet 4.3.7 ships a dependency-free `netstandard2.0` asset and supports MQTT 3.1.1 and 5.

The other broker adapters (`RabbitMq`, `Kafka`, `AzureServiceBus`, `Aws`) all carry a
`netstandard2.0` asset, and the constitution requires broad TFM reach without an ADR-backed
exception.

## Decision

Use MQTTnet 4.3.7.1207 and target `netstandard2.0`/`net10.0`/`net11.0`. The adapter sets MQTT 5
as the protocol because envelope metadata travels in user properties. MQTT 3.1.1-specific
metadata behavior is out of scope; a caller that overrides the protocol through
`ConfigureClient` accepts topic-only routing and loses the user-property metadata.

## Consequences

- Legacy .NET producers can reference the MQTT transport.
- The package carries a stable, widely deployed MQTTnet major version instead of the newest one.
- MQTT 5 features used by the adapter (user properties, content type) are available on all TFMs.
- Security fixes in MQTTnet 5 cannot be consumed until the package is retargeted or a separate
  modern-only asset is added.

## Revisit trigger

Revisit if MQTTnet 5 regains a `netstandard2.0` asset, if security guidance requires a 5.x-only
fix, or when a modern-only MQTT package can be shipped without breaking the current support
matrix. Target review date: **2027-03-31**.
