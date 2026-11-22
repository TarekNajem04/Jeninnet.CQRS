//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Aws;

/// <summary>
/// AWS region, endpoint, FIFO, visibility, and topology options for the SQS/SNS transport.
/// </summary>
public sealed class AwsTransportOptions {
    /// <summary>Gets or sets the AWS region. Defaults to <c>us-east-1</c>.</summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// Gets or sets an explicit service URL (LocalStack or a VPC endpoint). When
    /// <see langword="null"/> the standard AWS endpoint for <see cref="Region"/> is used.
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// Gets or sets whether queues/topics are FIFO. Defaults to <see langword="false"/>; FIFO
    /// names are suffixed with <c>.fifo</c> and <see cref="Transports.TransportMessageOptions.PartitionKey"/>
    /// becomes the message group id.
    /// </summary>
    public bool UseFifo { get; set; }

    /// <summary>Gets or sets whether queues/topics/subscriptions are created on start. Defaults to <see langword="true"/>.</summary>
    public bool EnsureTopology { get; set; } = true;

    /// <summary>Gets or sets the SQS visibility timeout. Defaults to 30 seconds.</summary>
    public TimeSpan VisibilityTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how often visibility is extended while a handler runs. Defaults to ten
    /// seconds and must be shorter than <see cref="VisibilityTimeout"/>.
    /// </summary>
    public TimeSpan VisibilityRenewalInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets or sets the SQS long-poll wait in seconds. Defaults to 1.</summary>
    public int ReceiveWaitSeconds { get; set; } = 1;

    /// <summary>Gets or sets the maximum messages per receive. Defaults to 10.</summary>
    public int MaxNumberOfMessages { get; set; } = 10;

    /// <summary>
    /// Gets or sets the inline payload ceiling in bytes. Defaults to 240 KiB; larger payloads
    /// must be moved to the claim-check store, and an inline body that still exceeds the SQS/SNS
    /// cap fails loudly instead of being truncated.
    /// </summary>
    public int PayloadLimitBytes { get; set; } = 240 * 1024;

    /// <summary>Gets the message types this transport consumes.</summary>
    public IList<AwsSubscription> Subscriptions { get; } = [];
}
