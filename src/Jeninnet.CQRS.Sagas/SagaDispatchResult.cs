//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas;

/// <summary>How an event affected a saga.</summary>
public enum SagaDispatchStatus {
    /// <summary>The event did not match an existing saga's transition table; nothing was written.</summary>
    Ignored = 0,

    /// <summary>An unknown correlation id created a new saga.</summary>
    Created = 1,

    /// <summary>An existing saga transitioned.</summary>
    Transitioned = 2,
}

/// <summary>
/// The observable outcome of dispatching one event to the saga dispatcher. Follow-up message
/// types are listed in execution order; the objects themselves are already dispatched.
/// </summary>
/// <param name="SagaId">The correlation id.</param>
/// <param name="Status">Created, transitioned, or ignored.</param>
/// <param name="State">The state after the event (the unchanged state when ignored).</param>
/// <param name="Published">Follow-up messages published or sent, in execution order.</param>
/// <param name="Scheduled">Follow-up messages deferred, in execution order.</param>
public sealed record SagaDispatchResult(
    string SagaId,
    SagaDispatchStatus Status,
    string State,
    IReadOnlyList<Type> Published,
    IReadOnlyList<Type> Scheduled);
