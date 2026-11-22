//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Testing;

/// <summary>
/// Non-generic view of a saga test result used by the assertion helpers. The typed state remains
/// available on <see cref="SagaTestResult{TState}"/>.
/// </summary>
public interface ISagaTestResult {
    /// <summary>Gets the correlation id.</summary>
    string SagaId { get; }

    /// <summary>Gets the dispatch outcome.</summary>
    SagaDispatchStatus Status { get; }

    /// <summary>Gets the persisted state name after the transition.</summary>
    string StateName { get; }

    /// <summary>Gets the follow-up commands and notifications in dispatch order.</summary>
    IReadOnlyList<object> Published { get; }
}

/// <summary>
/// One event run through the saga machine: the resulting logical state, the typed state when a row
/// exists, and every follow-up message the transition produced.
/// </summary>
/// <typeparam name="TState">The concrete saga state type.</typeparam>
/// <param name="SagaId">The correlation id.</param>
/// <param name="Status">Created, transitioned, or ignored.</param>
/// <param name="StateName">The persisted state name after the transition.</param>
/// <param name="State">The typed state, or <see langword="null"/> when no row exists.</param>
/// <param name="Published">Follow-up commands and notifications in dispatch order.</param>
public sealed record SagaTestResult<TState>(
    string SagaId,
    SagaDispatchStatus Status,
    string StateName,
    TState? State,
    IReadOnlyList<object> Published) : ISagaTestResult where TState : class, ISagaState;