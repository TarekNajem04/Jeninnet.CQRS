# Shared source

This folder follows the sibling `Jeninnet.Validation` shared-source pattern:

- each shared unit lives in its own folder with a `.projitems` file;
- consuming projects import the `.projitems` and compile the source into their own assembly;
- namespaces are stable and internal types stay internal to each consumer;
- there is no shared runtime assembly and no package dependency.

Current units:

| Unit | Consumers | Purpose |
|---|---|---|
| `Transport` | Core in-memory transport, AWS, Azure Service Bus, HTTP, Kafka, MQTT, RabbitMQ, SignalR | Builds envelopes, wraps send/publish observation, and owns reconnect and shutdown lifecycle so every transport reports and behaves the same. |
| `Transport.Aws` | AWS transport | Holds the SNS/SQS options, validated dependencies, envelope policy, and single-flight start state shared by both AWS adapters. |

This directory is repository infrastructure, not a NuGet package.
