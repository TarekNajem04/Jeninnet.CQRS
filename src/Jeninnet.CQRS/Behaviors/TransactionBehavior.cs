//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Behaviors;

/// <summary>
/// A pipeline behavior that wraps handler execution in an <see cref="ITransactionScope"/>:
/// the scope begins before the handler runs, commits on success, and rolls back on failure.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
/// Initializes a new instance with the given transaction scope and optional participants.
/// </remarks>
/// <param name="scope">The transaction scope. Never <see langword="null"/>.</param>
/// <param name="participants">
/// Participants whose side effects follow the transaction outcome (for example the notification
/// outbox). When omitted, the behavior is exactly the historical scope-only behavior.
/// </param>
public sealed class TransactionBehavior<TRequest, TResponse>(
    ITransactionScope scope,
    IEnumerable<ITransactionParticipant>? participants = null) : IOrderedPipelineBehavior<TRequest, TResponse> {
    private readonly ITransactionScope _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    private readonly ITransactionParticipant[] _participants = participants?.ToArray() ?? [];

    /// <inheritdoc />
    /// <remarks>
    /// Runs innermost among the built-in behaviors so validation, caching, and idempotency
    /// stay outside the transaction; user behaviors (<see cref="PipelineOrder.CUSTOM"/>) run
    /// outside it as well.
    /// </remarks>
    int IOrderedPipelineBehavior<TRequest, TResponse>.Order => PipelineOrder.TRANSACTION;

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(TRequest request, Func<TRequest, CancellationToken, Task<TResponse>> next, CancellationToken cancellationToken) {
        await _scope.BeginAsync(cancellationToken).ConfigureAwait(false);

        foreach(var participant in _participants) {
            await participant.BeginAsync(cancellationToken).ConfigureAwait(false);
        }

        var committed = false;

        try {
            var response = await next(request, cancellationToken).ConfigureAwait(false);
            await _scope.CommitAsync(cancellationToken).ConfigureAwait(false);
            committed = true;

            // Participants observe a successful commit only: publishing before this point could
            // expose a notification whose database writes the scope later rolled back.
            foreach(var participant in _participants) {
                await participant.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return response;
        }
        catch {
            // The handler failed: the original exception is what the caller must see.
            // A failing rollback must never mask it, so rollback errors are swallowed here.
            if(!committed) {
                try {
                    await _scope.RollbackAsync(cancellationToken).ConfigureAwait(false);
                }
                catch {
                    // Intentionally ignored: the handler exception below takes precedence.
                }
            }

            foreach(var participant in _participants) {
                try {
                    await participant.RollbackAsync(cancellationToken).ConfigureAwait(false);
                }
                catch {
                    // Intentionally ignored: the handler exception below takes precedence.
                }
            }

            throw;
        }
    }
}