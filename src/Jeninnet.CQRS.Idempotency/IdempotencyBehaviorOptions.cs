//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency;

/// <summary>
/// Options for <see cref="IdempotencyBehavior{TRequest, TResponse}"/>.
/// </summary>
public sealed class IdempotencyBehaviorOptions {
    /// <summary>
    /// Gets or sets how long a caller waits for another owner's execution before throwing
    /// <see cref="ConcurrentExecutionException"/>. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan WaitTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how long an execution lease stays valid without renewal. Defaults to
    /// 30 seconds; keep it above the handler's normal duration or call
    /// <see cref="IIdempotencyStore.RenewAsync"/> for long-running work.
    /// </summary>
    public TimeSpan LeaseTimeToLive { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how often a waiting caller re-checks the lease and stored response.
    /// Defaults to 50 milliseconds.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Gets or sets whether a busy lease throws <see cref="ConcurrentExecutionException"/>
    /// immediately instead of waiting up to <see cref="WaitTimeout"/>. Defaults to
    /// <see langword="false"/>.
    /// </summary>
    public bool FailFastWhenBusy { get; set; }

    /// <summary>
    /// Gets or sets the optional background renewal interval while the handler runs. Defaults to
    /// <see langword="null"/> (no heartbeat). When set, it must be shorter than
    /// <see cref="LeaseTimeToLive"/>; a long-running handler then keeps its lease alive instead
    /// of relying on a very large TTL.
    /// </summary>
    public TimeSpan? HeartbeatInterval { get; set; }

    internal void Validate() {
        if(WaitTimeout <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(WaitTimeout), WaitTimeout, "WaitTimeout must be greater than zero."); // NOSONAR: The property name is the public option key used in the validation error.
        }

        if(LeaseTimeToLive <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(LeaseTimeToLive), LeaseTimeToLive, "LeaseTimeToLive must be greater than zero."); // NOSONAR: The property name is the public option key used in the validation error.
        }

        if(PollInterval <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(PollInterval), PollInterval, "PollInterval must be greater than zero."); // NOSONAR: The property name is the public option key used in the validation error.
        }

        if(HeartbeatInterval is { } heartbeatInterval) {
            if(heartbeatInterval <= TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(HeartbeatInterval), heartbeatInterval, "HeartbeatInterval must be greater than zero when set."); // NOSONAR: The property name is the public option key used in the validation error.
            }

            if(heartbeatInterval >= LeaseTimeToLive) {
                throw new ArgumentOutOfRangeException(nameof(HeartbeatInterval), heartbeatInterval, "HeartbeatInterval must be shorter than LeaseTimeToLive."); // NOSONAR: The property name is the public option key used in the validation error.
            }
        }
    }
}