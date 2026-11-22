//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Behaviors;

/// <summary>
/// Joins a <see cref="TransactionBehavior{TRequest, TResponse}"/> unit of work without owning
/// the transaction itself. Participants begin after the scope begins, commit after the scope
/// commits, and roll back when handling fails.
/// </summary>
/// <remarks>
/// Ordering is the contract: committing participants run only after the scope commit succeeds,
/// so side effects (for example staging and publishing outbox notifications) never observe a
/// rolled-back transaction.
/// </remarks>
public interface ITransactionParticipant {
    /// <summary>Runs after the transaction scope has begun.</summary>
    Task BeginAsync(CancellationToken cancellationToken);

    /// <summary>Runs after the transaction scope has committed successfully.</summary>
    Task CommitAsync(CancellationToken cancellationToken);

    /// <summary>Runs when handling fails and the transaction will not commit.</summary>
    Task RollbackAsync(CancellationToken cancellationToken);
}