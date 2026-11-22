//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas;

/// <summary>
/// Thrown when a saga save loses the optimistic-concurrency race. The dispatcher retries bounded
/// by <see cref="SagaOptions.MaxConcurrencyRetries"/>; external callers see the final failure.
/// </summary>
public sealed class SagaConcurrencyException : InvalidOperationException {
    /// <summary>Initializes the exception for a lost version race.</summary>
    /// <param name="sagaId">The correlation id.</param>
    /// <param name="stateType">The concrete state type.</param>
    /// <param name="expectedVersion">The version the writer expected.</param>
    /// <param name="actualVersion">The version found, or -1 when an insert collided.</param>
    /// <param name="innerException">The provider exception, when available.</param>
    public SagaConcurrencyException(
        string sagaId,
        Type stateType,
        long expectedVersion,
        long actualVersion,
        Exception? innerException = null)
        : base(BuildConcurrencyMessage(sagaId, stateType, expectedVersion, actualVersion), innerException) {
        SagaId = sagaId;
        StateType = stateType;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    private static string BuildConcurrencyMessage(
        string sagaId,
        Type stateType,
        long expectedVersion,
        long actualVersion) {
        Guard.NotNull(sagaId);
        Guard.NotNull(stateType);

        return $"Saga '{sagaId}' ({stateType.Name}) changed concurrently: expected version {expectedVersion}, found {actualVersion}.";
    }

    /// <summary>Gets the correlation id.</summary>
    public string SagaId { get; }

    /// <summary>Gets the concrete state type.</summary>
    public Type StateType { get; }

    /// <summary>Gets the version the writer expected.</summary>
    public long ExpectedVersion { get; }

    /// <summary>Gets the version found, or -1 for an insert collision.</summary>
    public long ActualVersion { get; }
}

/// <summary>
/// Thrown when a machine's correlation function returns no id. A saga event without a correlation
/// id is a configuration error, not an ignorable out-of-order event.
/// </summary>
public sealed class SagaCorrelationException : InvalidOperationException {
    /// <summary>Initializes the exception for an empty correlation id.</summary>
    /// <param name="messageType">The event message type.</param>
    public SagaCorrelationException(Type messageType)
        : base(BuildCorrelationMessage(messageType)) {
        MessageType = messageType;
    }

    private static string BuildCorrelationMessage(Type messageType) {
        Guard.NotNull(messageType);

        return $"The saga correlation function for '{messageType.FullName}' returned an empty id.";
    }

    /// <summary>Gets the event message type whose correlation failed.</summary>
    public Type MessageType { get; }
}
