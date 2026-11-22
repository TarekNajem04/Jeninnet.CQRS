//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Behaviors;

/// <summary>
/// Default no-op <see cref="ITransactionScope"/>: keeps the pipeline honest when no real
/// transaction provider is registered. Replaced by the last registered
/// <see cref="ITransactionScope"/> implementation.
/// </summary>
internal sealed class NullTransactionScope : ITransactionScope {
    public Task BeginAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}