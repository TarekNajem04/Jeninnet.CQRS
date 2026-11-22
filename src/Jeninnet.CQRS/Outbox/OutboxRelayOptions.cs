//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Outbox;

/// <summary>
/// Options for the transport outbox relay. Defaults keep batches small, leases short enough for
/// fast crash recovery, and exponential backoff capped at five minutes.
/// </summary>
public sealed class OutboxRelayOptions {
    /// <summary>
    /// Gets or sets whether the transport relay owns the outbox. Set by
    /// <c>AddCqrsOutboxRelay&lt;TTransport&gt;()</c>; the in-process dispatcher and immediate
    /// publish path stand down while it is true.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets how many rows one poll leases. Defaults to 32.</summary>
    public int BatchSize { get; set; } = 32;

    /// <summary>Gets or sets how often an idle relay polls. Defaults to one second.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets how long a worker owns a leased row. Defaults to 30 seconds; the lease must
    /// exceed the normal broker publish time.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the first retry delay after a failed publish. Defaults to one second.</summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets the backoff ceiling. Defaults to five minutes.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets or sets the exponential backoff multiplier. Defaults to 2.</summary>
    public double BackoffMultiplier { get; set; } = 2;

    /// <summary>
    /// Gets or sets how many failed publishes move a row to the dead-letter store. Defaults to
    /// 10; the row remains inspectable with its last error.
    /// </summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>
    /// Gets or sets whether a failure stops the batch so later rows cannot overtake it. Defaults
    /// to <see langword="true"/>, preserving outbox order.
    /// </summary>
    public bool StopOnFailure { get; set; } = true;

    /// <summary>
    /// Gets or sets the lease owner id. <see langword="null"/> generates a process-unique id so
    /// two workers on one store never claim each other's rows.
    /// </summary>
    public string? OwnerId { get; set; }
}
