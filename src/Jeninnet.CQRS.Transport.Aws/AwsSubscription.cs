//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Aws;

/// <summary>
/// A message type this transport consumes. Send uses its queue; publish uses its SNS topic and
/// one subscriber queue per handler.
/// </summary>
public sealed class AwsSubscription {
    /// <summary>Gets or sets the message type to consume.</summary>
    public Type MessageType { get; set; } = typeof(object);

    /// <summary>Gets or sets an explicit SQS queue name; <see langword="null"/> uses the naming convention.</summary>
    public string? QueueName { get; set; }

    /// <summary>Gets or sets an explicit SNS topic name; <see langword="null"/> uses the naming convention.</summary>
    public string? TopicName { get; set; }
}