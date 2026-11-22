//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Behaviors;

/// <summary>
/// Defines a unit-of-work scope for the <see cref="TransactionBehavior{TRequest, TResponse}"/>.
/// </summary>
/// <remarks>
/// The behavior begins the scope before invoking the handler, commits it when the handler
/// succeeds, and rolls it back when the handler throws. Implementations are typically backed
/// by a database transaction (for example Entity Framework Core). The core library ships a
/// no-op default (<c>NullTransactionScope</c>); register your own implementation to make
/// transactions real.
/// </remarks>
public interface ITransactionScope {
    /// <summary>
    /// Begins the transaction scope.
    /// </summary>
    Task BeginAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Commits the transaction scope after successful handling.
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Rolls the transaction scope back after a handling failure.
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken);
}
