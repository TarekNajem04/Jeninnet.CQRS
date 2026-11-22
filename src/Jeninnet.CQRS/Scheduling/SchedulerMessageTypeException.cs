//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Scheduling;

/// <summary>Thrown when a scheduled message type cannot be resolved after a restart.</summary>
public sealed class SchedulerMessageTypeException : InvalidOperationException {
    /// <summary>Initializes the exception.</summary>
    /// <param name="messageType">The unresolved type name.</param>
    /// <param name="scheduleKey">The schedule key.</param>
    public SchedulerMessageTypeException(string messageType, string scheduleKey)
        : base($"Scheduled message type '{messageType}' for schedule '{scheduleKey}' could not be resolved.") {
        MessageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
        ScheduleKey = scheduleKey ?? throw new ArgumentNullException(nameof(scheduleKey));
    }

    /// <summary>Gets the unresolved type name.</summary>
    public string MessageType { get; }

    /// <summary>Gets the schedule key.</summary>
    public string ScheduleKey { get; }
}